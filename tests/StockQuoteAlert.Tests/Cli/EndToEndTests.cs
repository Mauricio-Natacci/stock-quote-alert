using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using StockQuoteAlert.Cli;
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

    private readonly FakeQuoteApi _quoteApi = new();
    private readonly FakeSmtpServer _smtp = new();
    private readonly FakeTimeProvider _time = new(TestData.Now);
    private readonly CapturingLoggerProvider _logs = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private int ChecksDone => _logs.Entries.Count(entry => entry.Message.StartsWith("PETR4 quoted at ", StringComparison.Ordinal));

    [Fact]
    public async Task TradingSession_SendsOneEmailPerZoneCrossingAndRetriesFailedDeliveries()
    {
        _quoteApi.RespondWithPrices(22.63m, 22.70m, 22.75m, 22.63m, 22.50m, 22.63m, 22.70m, 22.71m);
        using var host = CreateHost();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
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

        await NextTickAsync(6);
        _smtp.ConnectionFailure = new IOException("Connection refused");
        await NextTickAsync(7);
        await Eventually.HoldsAsync(() => _logs.Entries.Any(entry => entry.Message.Contains("was not delivered", StringComparison.Ordinal)));
        Assert.Equal(2, _smtp.Delivered.Count);

        _smtp.ConnectionFailure = null;
        await NextTickAsync(8);
        await Eventually.HoldsAsync(() => _smtp.Delivered.Count == 3);

        await stop.CancelAsync();
        Assert.Equal(ExitCodes.Success, await run.WaitAsync(RunTimeout, Ct));

        Assert.Equal(
            [
                "[VENDA] PETR4 subiu para R$ 22,70, 0,13% acima da referência de venda",
                "[COMPRA] PETR4 caiu para R$ 22,50, 0,40% abaixo da referência de compra",
                "[VENDA] PETR4 subiu para R$ 22,71, 0,18% acima da referência de venda",
            ],
            _smtp.Delivered.Select(email => email.Subject));
        Assert.All(_smtp.Delivered, email =>
        {
            Assert.Equal("dest@gmail.com", email.To);
            Assert.Equal("me@gmail.com", email.FromAddress);
            Assert.Contains("29/10/2026 14:30 (horário de Brasília)", email.Body, StringComparison.Ordinal);
        });
        Assert.Equal(8, _quoteApi.Requests.Count);
        Assert.All(_quoteApi.Requests, request =>
            Assert.Equal("https://query1.finance.yahoo.com/v8/finance/chart/PETR4.SA?interval=1d&range=1d", request.Uri.ToString()));
        Assert.All(_smtp.Connections, connection => Assert.Equal(new SmtpConnection("smtp.gmail.com", 587, MailKit.Security.SecureSocketOptions.StartTls), connection));
        Assert.Contains(_logs.Entries, entry => entry.Message == "Stopped monitoring PETR4");
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
        Assert.Contains(_logs.Entries, entry => entry.Level == LogLevel.Critical && entry.Message == "Unrecoverable failure while monitoring PETR4; shutting down");
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

    public void Dispose() => _quoteApi.Dispose();

    private IHost CreateHost(Dictionary<string, string?>? overrides = null)
    {
        // The shipped appsettings.json is loaded by the host; these stand in for user-secrets.
        var secrets = AppSettings.Valid;

        foreach (var (key, value) in overrides ?? [])
        {
            secrets[key] = value;
        }

        return StockQuoteAlertApp.Create(new MonitoringTarget(Ticker.Create("PETR4"), new AlertThresholds(22.67m, 22.59m)), builder =>
        {
            builder.Configuration.AddInMemoryCollection(secrets);
            builder.Logging.ClearProviders().AddProvider(_logs);
            builder.Services.AddSingleton<TimeProvider>(_time);
            builder.Services.AddHttpClient(InfrastructureServices.YahooClientName).ConfigurePrimaryHttpMessageHandler(() => _quoteApi);
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