using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StockQuoteAlert.Cli;
using StockQuoteAlert.Core.Application.Monitoring;
using StockQuoteAlert.Infrastructure.Email;
using StockQuoteAlert.Tests.TestDoubles;
using InfrastructureServices = StockQuoteAlert.Infrastructure.DependencyInjection;

namespace StockQuoteAlert.Tests.Cli;

/// <summary>Runs the real host end to end; only the quote API and the SMTP server are faked.</summary>
public sealed class StockQuoteAlertAppTests : IDisposable
{
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(10);

    private readonly FakeQuoteApi _quoteApi = new();
    private readonly FakeSmtpServer _smtp = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RunAsync_UnknownTicker_ReturnsUnrecoverableFailureExitCode()
    {
        _quoteApi.RespondWithStatus(HttpStatusCode.NotFound);
        using var host = CreateHost();

        var exitCode = await StockQuoteAlertApp.RunAsync(host, Ct).WaitAsync(RunTimeout, Ct);

        Assert.Equal(ExitCodes.UnrecoverableMonitoringFailure, exitCode);
        Assert.Single(_quoteApi.Requests);
        Assert.Empty(_smtp.Clients);
    }

    [Fact]
    public async Task RunAsync_StoppedByTheUser_ReturnsSuccess()
    {
        using var host = CreateHost();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var run = StockQuoteAlertApp.RunAsync(host, stop.Token);
        Assert.True(await _quoteApi.RequestSignal.WaitAsync(RunTimeout, Ct));
        await stop.CancelAsync();

        Assert.Equal(ExitCodes.Success, await run.WaitAsync(RunTimeout, Ct));
    }

    [Fact]
    public async Task RunAsync_TransientFailures_KeepMonitoring()
    {
        // A recoverable failure that the resilience handler does not retry, so the test stays fast.
        _quoteApi.RespondWithStatus(HttpStatusCode.BadRequest);

        using var host = CreateHost();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        // Resolved before running, like the app does: RunAsync disposes the host's services when it ends.
        var outcome = host.Services.GetRequiredService<MonitoringOutcome>();
        var run = StockQuoteAlertApp.RunAsync(host, stop.Token);
        Assert.True(await _quoteApi.RequestSignal.WaitAsync(RunTimeout, Ct));
        await Task.Delay(200, Ct);

        Assert.False(run.IsCompleted);
        await stop.CancelAsync();
        Assert.Equal(ExitCodes.Success, await run.WaitAsync(RunTimeout, Ct));
        Assert.False(outcome.StoppedByUnrecoverableFailure);
    }

    [Fact]
    public async Task RunAsync_PriceAboveSell_SendsTheAlertEmail()
    {
        _quoteApi.RespondWithPrices(22.70m);
        using var host = CreateHost();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var run = StockQuoteAlertApp.RunAsync(host, stop.Token);
        await Eventually.HoldsAsync(() => !_smtp.Delivered.IsEmpty);
        await stop.CancelAsync();
        await run.WaitAsync(RunTimeout, Ct);

        var email = Assert.Single(_smtp.Delivered);
        Assert.Equal("dest@gmail.com", email.To);
        Assert.StartsWith("[VENDA] PETR4 subiu para R$ 22,70", email.Subject, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_AppliesTheConfigureCallback()
    {
        var called = false;

        using var host = StockQuoteAlertApp.Create(TestData.Target, builder =>
        {
            called = true;
            builder.Configuration.AddInMemoryCollection(AppSettings.Valid);
        });

        Assert.True(called);
        Assert.Equal(TestData.Target, host.Services.GetRequiredService<StockMonitor>().Target);
    }

    [Fact]
    public async Task RunAsync_WithNullHost_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => StockQuoteAlertApp.RunAsync(null!, Ct));
    }

    public void Dispose() => _quoteApi.Dispose();

    private IHost CreateHost() => StockQuoteAlertApp.Create(TestData.Target, builder =>
    {
        builder.Configuration.AddInMemoryCollection(AppSettings.Valid);
        builder.Logging.ClearProviders();
        builder.Services.AddHttpClient(InfrastructureServices.YahooClientName).ConfigurePrimaryHttpMessageHandler(() => _quoteApi);
        builder.Services.AddSingleton<ISmtpClientFactory>(_smtp);
    });
}