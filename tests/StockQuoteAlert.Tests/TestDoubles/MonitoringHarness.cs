using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using StockQuoteAlert.Core.Application.Monitoring;
using StockQuoteAlert.Infrastructure.Email;
using StockQuoteAlert.Infrastructure.Quotes;
using StockQuoteAlert.Infrastructure.Quotes.Yahoo;

namespace StockQuoteAlert.Tests.TestDoubles;

/// <summary>
/// Wires the real monitoring object graph (Yahoo source, adapter, composer, MailKit sender, e-mail notifier, monitor),
/// replacing only the external boundaries: the quote HTTP API, the SMTP server, the clock and the logs.
/// </summary>
internal sealed class MonitoringHarness : IDisposable
{
    private readonly HttpClient _httpClient;

    /// <param name="targets">PETR4 (sell above 22.67, buy below 22.59) when none is given.</param>
    public MonitoringHarness(params MonitoringTarget[] targets)
    {
        _httpClient = QuoteApi.CreateClient();

        var emailOptions = Options.Create(TestData.EmailOptions);
        var sender = new MailKitEmailSender(Smtp, Options.Create(TestData.SmtpOptions), emailOptions);
        var notifier = new EmailAlertNotifier(new AlertEmailComposer(), sender, emailOptions, NotifierLog);
        var quoteProvider = new QuoteSourceAdapter(new YahooFinanceQuoteSource(_httpClient, Options.Create(new YahooFinanceOptions())));

        MonitoringTarget[] monitored = targets.Length == 0 ? [TestData.Target] : targets;
        Monitors = [.. monitored.Select(target => new StockMonitor(quoteProvider, notifier, target, MonitorLog))];
    }

    public FakeQuoteApi QuoteApi { get; } = new();

    public FakeSmtpServer Smtp { get; } = new();

    public FakeTimeProvider Time { get; } = new(TestData.Now);

    public FakeLogger<StockMonitor> MonitorLog { get; } = new();

    public FakeLogger<MonitoringWorker> WorkerLog { get; } = new();

    public MonitoringOutcome Outcome { get; } = new();

    public FakeLogger<EmailAlertNotifier> NotifierLog { get; } = new();

    /// <summary>One monitor per target, sharing the same quote API, SMTP server and logs.</summary>
    public IReadOnlyList<StockMonitor> Monitors { get; }

    /// <summary>The first monitor (PETR4 by default).</summary>
    public StockMonitor Monitor => Monitors[0];

    public MonitoringWorker CreateWorker(IHostApplicationLifetime lifetime, TimeSpan pollingInterval) => new(
        Monitors,
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

    public void Dispose()
    {
        _httpClient.Dispose();
        QuoteApi.Dispose();
    }
}