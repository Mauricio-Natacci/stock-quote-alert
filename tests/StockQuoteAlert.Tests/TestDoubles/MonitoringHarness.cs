using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using StockQuoteAlert.Core.Application.Alerts;
using StockQuoteAlert.Core.Application.Monitoring;
using StockQuoteAlert.Infrastructure.Email;
using StockQuoteAlert.Infrastructure.Journal;
using StockQuoteAlert.Infrastructure.Messaging;
using StockQuoteAlert.Infrastructure.Quotes;
using StockQuoteAlert.Infrastructure.Quotes.Yahoo;

namespace StockQuoteAlert.Tests.TestDoubles;

/// <summary>
/// Wires the real monitoring object graph (Yahoo source, adapter, monitor, alert queue, dispatcher, e-mail consumer
/// with the real composer and MailKit sender) with a real in-memory journal, replacing only the external boundaries:
/// the quote HTTP API, the SMTP server, the clock and the logs. Published alerts stay queued until
/// <see cref="DeliverPendingAlertsAsync"/> (or until the dispatcher is started).
/// </summary>
internal sealed class MonitoringHarness : IDisposable
{
    private static readonly TimeSpan ClockStep = TimeSpan.FromSeconds(1);

    private readonly HttpClient _httpClient;
    private readonly List<PriceAlertRaised> _published = [];
    private int _delivered;

    /// <param name="targets">PETR4 (sell above 22.67, buy below 22.59) when none is given.</param>
    public MonitoringHarness(params MonitoringTarget[] targets)
    {
        _httpClient = QuoteApi.CreateClient();

        var emailOptions = Options.Create(TestData.EmailOptions);
        var sender = new MailKitEmailSender(Smtp, Options.Create(TestData.SmtpOptions), emailOptions);
        EmailConsumer = new EmailAlertConsumer(new AlertEmailComposer(emailOptions), sender, Journal, emailOptions, Time, EmailLog);
        Dispatcher = new AlertDispatcher(Queue, [EmailConsumer], Time, DispatcherLog);
        var quoteProvider = new QuoteSourceAdapter(new YahooFinanceQuoteSource(_httpClient, Options.Create(new YahooFinanceOptions())));

        MonitoringTarget[] monitored = targets.Length == 0 ? [TestData.Target] : targets;
        Monitors = [.. monitored.Select(target => new StockMonitor(quoteProvider, Queue, Journal, target, Time, MonitorLog))];
    }

    public FakeQuoteApi QuoteApi { get; } = new();

    public FakeSmtpServer Smtp { get; } = new();

    public FakeTimeProvider Time { get; } = new(TestData.Now);

    public InMemoryMonitoringJournal Journal { get; } = new();

    public InMemoryAlertQueue Queue { get; } = new();

    public AlertDispatcher Dispatcher { get; }

    public EmailAlertConsumer EmailConsumer { get; }

    public FakeLogger<StockMonitor> MonitorLog { get; } = new();

    public FakeLogger<MonitoringWorker> WorkerLog { get; } = new();

    public FakeLogger<AlertDispatcher> DispatcherLog { get; } = new();

    public FakeLogger<EmailAlertConsumer> EmailLog { get; } = new();

    public MonitoringOutcome Outcome { get; } = new();

    /// <summary>One monitor per target, sharing the same quote API, queue, SMTP server and logs.</summary>
    public IReadOnlyList<StockMonitor> Monitors { get; }

    /// <summary>The first monitor (PETR4 by default).</summary>
    public StockMonitor Monitor => Monitors[0];

    /// <summary>Every alert published so far, in order (reading them takes them out of the queue).</summary>
    public IReadOnlyList<PriceAlertRaised> Published
    {
        get
        {
            while (Queue.Reader.TryRead(out var alert))
            {
                _published.Add(alert);
            }

            return _published;
        }
    }

    public MonitoringWorker CreateWorker(IHostApplicationLifetime lifetime, TimeSpan pollingInterval) => new(
        Monitors,
        Journal,
        lifetime,
        Outcome,
        Options.Create(new MonitoringOptions { PollingInterval = pollingInterval }),
        Time,
        WorkerLog);

    /// <summary>Runs one check of the first monitor per price, in order.</summary>
    public async Task CheckPricesAsync(params decimal[] prices)
    {
        QuoteApi.RespondWithPrices(prices);

        foreach (var _ in prices)
        {
            await Monitor.CheckAsync(TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    /// Delivers, in order, every published alert not delivered yet, advancing the clock through retry delays when needed.
    /// </summary>
    public async Task DeliverPendingAlertsAsync()
    {
        var published = Published;

        for (; _delivered < published.Count; _delivered++)
        {
            var delivery = Dispatcher.DispatchAsync(published[_delivered], TestContext.Current.CancellationToken);
            await AdvanceUntilAsync(() => delivery.IsCompleted);
            await delivery;
        }
    }

    /// <summary>Moves the clock forward in small steps until <paramref name="condition"/> holds (retry delays elapse).</summary>
    public Task AdvanceUntilAsync(Func<bool> condition) =>
        Eventually.HoldsAsync(() =>
        {
            if (condition())
            {
                return true;
            }

            Time.Advance(ClockStep);
            return condition();
        });

    public void Dispose()
    {
        Dispatcher.Dispose();
        _httpClient.Dispose();
        QuoteApi.Dispose();
    }
}