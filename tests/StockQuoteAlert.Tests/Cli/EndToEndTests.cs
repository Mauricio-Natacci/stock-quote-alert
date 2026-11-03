using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using StockQuoteAlert.Cli;
using StockQuoteAlert.Core.Application.Abstractions;
using StockQuoteAlert.Core.Application.Journal;
using StockQuoteAlert.Core.Application.Monitoring;
using StockQuoteAlert.Core.Domain;
using StockQuoteAlert.Infrastructure.Email;
using StockQuoteAlert.Tests.TestDoubles;
using InfrastructureServices = StockQuoteAlert.Infrastructure.DependencyInjection;

namespace StockQuoteAlert.Tests.Cli;

/// <summary>
/// Runs the whole app (shipped appsettings.json, real host, real object graph) for PETR4 with sell above 22.67 and
/// buy below 22.59. Only the quote API, the SMTP server and the clock are faked; time moves one polling interval at a time.
/// </summary>
public sealed class EndToEndTests : IDisposable
{
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan AlertRetryDelay = TimeSpan.FromSeconds(5);

    private readonly FakeQuoteApi _quoteApi = new();
    private readonly FakeQuoteApi _brapi = FakeQuoteApi.Brapi();
    private readonly FakeSmtpServer _smtp = new();
    private readonly FakeTimeProvider _time = new(TestData.Now);
    private readonly CapturingLoggerProvider _logs = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private int ChecksDone => _logs.Entries.Count(entry => entry.Message.StartsWith("PETR4 quoted at ", StringComparison.Ordinal));

    [Fact]
    public async Task TradingSession_SendsOneEmailPerZoneCrossingAndRetriesAFailedEmail()
    {
        _quoteApi.RespondWithPrices(22.63m, 22.70m, 22.75m, 22.63m, 22.50m, 22.63m, 22.70m, 22.71m);
        using var host = CreateHost();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        // Resolved before running: RunAsync disposes the host's services when it ends.
        var journal = host.Services.GetRequiredService<IMonitoringJournal>();
        var run = StockQuoteAlertApp.RunAsync(host, stop.Token);

        await CheckCompletedAsync(1);
        Assert.Empty(_smtp.Delivered);

        await NextTickAsync(2);
        await Eventually.HoldsAsync(() => _smtp.Delivered.Count == 1);

        await NextTickAsync(3);
        await NextTickAsync(4);
        Assert.Single(_smtp.Delivered);

        await NextTickAsync(5);
        await Eventually.HoldsAsync(() => _smtp.Delivered.Count == 2);

        // SMTP down when the price crosses again: the dispatcher retries the same alert 5 s later.
        await NextTickAsync(6);
        _smtp.ConnectionFailure = new IOException("Connection refused");
        await NextTickAsync(7);
        await Eventually.HoldsAsync(() => _logs.Entries.Any(entry => entry.Message.Contains("retrying in 5s", StringComparison.Ordinal)));
        Assert.Equal(2, _smtp.Delivered.Count);

        _smtp.ConnectionFailure = null;
        _time.Advance(AlertRetryDelay);
        await Eventually.HoldsAsync(() => _smtp.Delivered.Count == 3);

        // Still above the sell reference: no new alert.
        await NextTickAsync(8);

        // While the app runs, the session is visible through the JSON API.
        using var client = host.GetTestClient();
        var health = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/health", UriKind.Relative), Ct);
        var dashboard = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/dashboard", UriKind.Relative), Ct);
        Assert.Equal("healthy", health.GetProperty("status").GetString());
        Assert.Equal(
            [22.63m, 22.70m, 22.75m, 22.63m, 22.50m, 22.63m, 22.70m, 22.71m],
            dashboard.GetProperty("quotes").EnumerateArray().Select(quote => quote.GetProperty("price").GetDecimal()));
        Assert.Equal(
            ["Sent", "Sent", "Failed", "Sent"],
            dashboard.GetProperty("notifications").EnumerateArray().Select(notification => notification.GetProperty("status").GetString()));
        Assert.Equal(
            ["Sell", "Buy", "Sell", "Sell"],
            dashboard.GetProperty("notifications").EnumerateArray().Select(notification => notification.GetProperty("action").GetString()));
        Assert.Equal(0, dashboard.GetProperty("lastFailures").GetArrayLength());
        Assert.Equal("PETR4", dashboard.GetProperty("configuration").GetProperty("targets")[0].GetProperty("ticker").GetString());

        await stop.CancelAsync();
        Assert.Equal(ExitCodes.Success, await run.WaitAsync(RunTimeout, Ct));
        Assert.DoesNotContain(_logs.Entries, entry => entry.EventName == "CheckCompleted");

        Assert.Equal(
            [
                "[VENDA] PETR4 subiu para R$ 22,70, 0,13% acima da referência de venda",
                "[COMPRA] PETR4 caiu para R$ 22,50, 0,40% abaixo da referência de compra",
                "[VENDA] PETR4 subiu para R$ 22,70, 0,13% acima da referência de venda",
            ],
            _smtp.Delivered.Select(email => email.Subject));
        Assert.All(_smtp.Delivered, email =>
        {
            Assert.Equal("dest@gmail.com", email.To);
            Assert.Equal("me@gmail.com", email.FromAddress);
            Assert.Contains("29/10/2026 14:30 (horário de Brasília)", email.Body, StringComparison.Ordinal);
            Assert.True(email.IsAlternative);
            Assert.Contains("href=\"https://homebroker.example.com/negociar?ativo=PETR4", email.HtmlBody, StringComparison.Ordinal);
        });
        Assert.Equal(8, _quoteApi.Requests.Count);
        Assert.All(_quoteApi.Requests, request =>
            Assert.Equal("https://query1.finance.yahoo.com/v8/finance/chart/PETR4.SA?interval=1d&range=1d", request.Uri.ToString()));
        Assert.All(_smtp.Connections, connection => Assert.Equal(new SmtpConnection("smtp.gmail.com", 587, MailKit.Security.SecureSocketOptions.StartTls), connection));
        Assert.Contains(_logs.Entries, entry => entry.Message == "Stopped monitoring PETR4");

        // Every alert also reaches the example consumers once, whatever happened to its e-mail.
        Assert.Equal(3, _logs.Entries.Count(entry => entry.Category.EndsWith(".SlackAlertConsumer", StringComparison.Ordinal)));
        Assert.Equal(3, _logs.Entries.Count(entry => entry.Category.EndsWith(".SmsAlertConsumer", StringComparison.Ordinal)));

        var snapshot = journal.GetSnapshot();
        Assert.Equal([22.63m, 22.70m, 22.75m, 22.63m, 22.50m, 22.63m, 22.70m, 22.71m], snapshot.Quotes.Select(quote => quote.Quote.Price));
        Assert.Equal(TestData.Now + (PollingInterval * 7) + AlertRetryDelay, snapshot.Quotes[^1].ObservedAt);
        Assert.Equal(
            [NotificationStatus.Sent, NotificationStatus.Sent, NotificationStatus.Failed, NotificationStatus.Sent],
            snapshot.Notifications.Select(notification => notification.Status));
        Assert.Equal("Connection refused", snapshot.Notifications[2].Error);
        Assert.Empty(snapshot.LastFailures);
    }

    [Fact]
    public async Task QuoteApiOutage_IsLoggedAndMonitoringRecovers()
    {
        _quoteApi.RespondWithStatus(HttpStatusCode.BadRequest).RespondWithPrices(22.70m);
        using var host = CreateHost();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var run = StockQuoteAlertApp.RunAsync(host, stop.Token);

        await Eventually.HoldsAsync(() => _logs.Entries.Any(entry => entry.Level == LogLevel.Error));
        _time.Advance(PollingInterval);
        await Eventually.HoldsAsync(() => _smtp.Delivered.Count == 1);

        await stop.CancelAsync();
        Assert.Equal(ExitCodes.Success, await run.WaitAsync(RunTimeout, Ct));
        Assert.Equal("Failed to check PETR4; retrying on next tick", Assert.Single(_logs.Entries, entry => entry.Level == LogLevel.Error).Message);
    }

    [Fact]
    public async Task UnknownTicker_StopsTheAppWithUnrecoverableFailureExitCode()
    {
        _quoteApi.RespondWithStatus(HttpStatusCode.NotFound);
        using var host = CreateHost();

        var exitCode = await StockQuoteAlertApp.RunAsync(host, Ct).WaitAsync(RunTimeout, Ct);

        Assert.Equal(ExitCodes.UnrecoverableMonitoringFailure, exitCode);
        Assert.Single(_quoteApi.Requests);
        Assert.Empty(_smtp.Clients);
        Assert.Equal(
            ["Unrecoverable failure while monitoring PETR4; it will no longer be checked", "No stock left to monitor; shutting down"],
            _logs.Entries.Where(entry => entry.Level == LogLevel.Critical).Select(entry => entry.Message));
    }

    [Fact]
    public async Task ConfiguredPollingInterval_IsUsed()
    {
        using var host = CreateHost(new() { ["Monitoring:PollingInterval"] = "00:05:00" });
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var run = StockQuoteAlertApp.RunAsync(host, stop.Token);

        Assert.True(await _quoteApi.RequestSignal.WaitAsync(RunTimeout, Ct));
        await CheckCompletedAsync(1);
        _time.Advance(PollingInterval);
        Assert.False(await _quoteApi.RequestSignal.WaitAsync(TimeSpan.FromMilliseconds(200), Ct));

        _time.Advance(TimeSpan.FromMinutes(5) - PollingInterval);
        await CheckCompletedAsync(2);

        await stop.CancelAsync();
        await run.WaitAsync(RunTimeout, Ct);
        Assert.Contains(_logs.Entries, entry => entry.Message.EndsWith("every 5 minutes", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SeveralStocks_SendSeparateEmailsAndKeepGoingWhenOneTickerIsUnknown()
    {
        _quoteApi
            .RespondWithPricesFor("PETR4", 22.63m, 22.70m)
            .RespondWithPricesFor("VALE3", 60.00m)
            .RespondWithStatusFor("ITUB4", HttpStatusCode.NotFound);
        var itub = new MonitoringTarget(Ticker.Create("ITUB4"), new AlertThresholds(40m, 38.50m));
        using var host = CreateHost(targets: new MonitoringTargets([TestData.Target, TestData.ValeTarget, itub]));
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var run = StockQuoteAlertApp.RunAsync(host, stop.Token);

        await Eventually.HoldsAsync(() => _smtp.Delivered.Count == 1 && ChecksDone >= 1);
        _time.Advance(PollingInterval);
        await Eventually.HoldsAsync(() => _smtp.Delivered.Count == 2);

        await stop.CancelAsync();
        Assert.Equal(ExitCodes.Success, await run.WaitAsync(RunTimeout, Ct));

        Assert.Equal(
            ["[COMPRA] VALE3 caiu para R$ 60,00, 1,32% abaixo da referência de compra", "[VENDA] PETR4 subiu para R$ 22,70, 0,13% acima da referência de venda"],
            _smtp.Delivered.Select(email => email.Subject));
        Assert.DoesNotContain("PETR4", _smtp.Delivered.First().Body, StringComparison.Ordinal);
        Assert.DoesNotContain("VALE3", _smtp.Delivered.Last().Body, StringComparison.Ordinal);
        Assert.Equal(1, _quoteApi.RequestsFor("ITUB4"));
        Assert.Equal(2, _quoteApi.RequestsFor("VALE3"));
        Assert.Contains(_logs.Entries, entry => entry.Message == "Unrecoverable failure while monitoring ITUB4; it will no longer be checked");

        // Each check closes with a separator, after the quotes of every stock checked in it.
        var log = _logs.Entries.ToList();
        var separators = log.Select((entry, index) => (entry, index)).Where(item => item.entry.EventName == "CheckCompleted").Select(item => item.index).ToList();
        Assert.Equal(2, separators.Count);
        var firstCheck = log.Take(separators[0]).Select(entry => entry.Message).ToList();
        Assert.Contains(firstCheck, message => message.StartsWith("PETR4 quoted at ", StringComparison.Ordinal));
        Assert.Contains(firstCheck, message => message.StartsWith("VALE3 quoted at ", StringComparison.Ordinal));
        Assert.DoesNotContain(_logs.Entries, entry => entry.Message.StartsWith("No stock left", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EveryTickerUnknown_StopsTheAppWithUnrecoverableFailureExitCode()
    {
        _quoteApi.RespondWithStatusFor("PETR4", HttpStatusCode.NotFound).RespondWithStatusFor("VALE3", HttpStatusCode.NotFound);
        using var host = CreateHost(targets: TestData.PetrAndVale);

        var exitCode = await StockQuoteAlertApp.RunAsync(host, Ct).WaitAsync(RunTimeout, Ct);

        Assert.Equal(ExitCodes.UnrecoverableMonitoringFailure, exitCode);
        Assert.Equal(1, _quoteApi.RequestsFor("PETR4"));
        Assert.Equal(1, _quoteApi.RequestsFor("VALE3"));
    }

    [Fact]
    public async Task YahooDown_QuotesComeFromBrapiAndAlertsStillGoOut()
    {
        _quoteApi.RespondWithStatus(HttpStatusCode.BadRequest).RespondWithStatus(HttpStatusCode.BadRequest);
        _brapi.RespondWithPrices(22.63m, 22.70m);
        using var host = CreateHost(useShippedFallback: true);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var run = StockQuoteAlertApp.RunAsync(host, stop.Token);

        await CheckCompletedAsync(1);
        await NextTickAsync(2);
        await Eventually.HoldsAsync(() => _smtp.Delivered.Count == 1);

        await stop.CancelAsync();
        Assert.Equal(ExitCodes.Success, await run.WaitAsync(RunTimeout, Ct));

        Assert.StartsWith("[VENDA] PETR4 subiu para R$ 22,70", Assert.Single(_smtp.Delivered).Subject, StringComparison.Ordinal);
        Assert.Equal(2, _quoteApi.Requests.Count);
        Assert.Equal(2, _brapi.Requests.Count);
        Assert.Contains(_logs.Entries, entry => entry.Message == "PETR4 quoted at 22.70 by brapi.dev (above sell 22.67)");
        Assert.Equal(
            2,
            _logs.Entries.Count(entry => entry.Level == LogLevel.Warning && entry.Message.StartsWith("Yahoo Finance failed for PETR4, using brapi.dev:", StringComparison.Ordinal)));
        Assert.DoesNotContain(_logs.Entries, entry => entry.Level >= LogLevel.Error);
    }

    [Fact]
    public async Task TransientFailure_IsVisibleOnTheDashboard()
    {
        _quoteApi.RespondWithStatus(HttpStatusCode.BadRequest);
        using var host = CreateHost();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var run = StockQuoteAlertApp.RunAsync(host, stop.Token);

        await Eventually.HoldsAsync(() => _logs.Entries.Any(entry => entry.Level == LogLevel.Error));
        using var client = host.GetTestClient();
        var dashboard = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/dashboard", UriKind.Relative), Ct);

        await stop.CancelAsync();
        await run.WaitAsync(RunTimeout, Ct);
        var failure = Assert.Single(dashboard.GetProperty("lastFailures").EnumerateArray());
        Assert.Equal("PETR4", failure.GetProperty("ticker").GetString());
        Assert.Equal("Transient", failure.GetProperty("kind").GetString());
        Assert.Equal("Yahoo Finance responded with HTTP 400 for PETR4.", failure.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task TickerUnknownToBothProviders_StopsTheAppWithUnrecoverableFailureExitCode()
    {
        _quoteApi.RespondWithStatus(HttpStatusCode.NotFound);
        _brapi.RespondWithStatus(HttpStatusCode.NotFound);
        using var host = CreateHost(useShippedFallback: true);

        var exitCode = await StockQuoteAlertApp.RunAsync(host, Ct).WaitAsync(RunTimeout, Ct);

        Assert.Equal(ExitCodes.UnrecoverableMonitoringFailure, exitCode);
        var critical = _logs.Entries.First(entry => entry.Level == LogLevel.Critical);
        Assert.StartsWith("All quote providers failed for PETR4.", critical.Exception!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task YahooDownAndBrapiWithoutToken_KeepsMonitoring()
    {
        _quoteApi.RespondWithStatus(HttpStatusCode.BadRequest);
        _brapi.RespondWithStatus(HttpStatusCode.Unauthorized);
        using var host = CreateHost(useShippedFallback: true);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var run = StockQuoteAlertApp.RunAsync(host, stop.Token);

        await Eventually.HoldsAsync(() => _logs.Entries.Any(entry => entry.Level == LogLevel.Error));
        _time.Advance(PollingInterval);
        await CheckCompletedAsync(1);

        await stop.CancelAsync();
        Assert.Equal(ExitCodes.Success, await run.WaitAsync(RunTimeout, Ct));
        Assert.DoesNotContain(_logs.Entries, entry => entry.Level == LogLevel.Critical);
    }

    public void Dispose()
    {
        _quoteApi.Dispose();
        _brapi.Dispose();
    }

    /// <param name="useShippedFallback">Keeps the shipped brapi fallback, answered by the brapi fake.</param>
    private WebApplication CreateHost(Dictionary<string, string?>? overrides = null, MonitoringTargets? targets = null, bool useShippedFallback = false)
    {
        // The shipped appsettings.json is loaded by the host; these stand in for user-secrets.
        var secrets = useShippedFallback ? AppSettings.Valid : AppSettings.WithoutFallback;

        foreach (var (key, value) in overrides ?? [])
        {
            secrets[key] = value;
        }

        return StockQuoteAlertApp.Create(targets ?? new MonitoringTargets([new MonitoringTarget(Ticker.Create("PETR4"), new AlertThresholds(22.67m, 22.59m))]), builder =>
        {
            builder.Configuration.AddInMemoryCollection(secrets);
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders().AddProvider(_logs);
            builder.Services.AddSingleton<TimeProvider>(_time);
            builder.Services.AddHttpClient(InfrastructureServices.YahooClientName).ConfigurePrimaryHttpMessageHandler(() => _quoteApi);
            builder.Services.AddHttpClient(InfrastructureServices.BrapiClientName).ConfigurePrimaryHttpMessageHandler(() => _brapi);
            builder.Services.AddSingleton<ISmtpClientFactory>(_smtp);
        });
    }

    private async Task NextTickAsync(int check)
    {
        _time.Advance(PollingInterval);
        await CheckCompletedAsync(check);
    }

    private Task CheckCompletedAsync(int check) => Eventually.HoldsAsync(() => ChecksDone >= check);
}