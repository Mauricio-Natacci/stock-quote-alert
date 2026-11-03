using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
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
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Cli.Dashboard;

/// <summary>Calls the JSON API of the real app. Monitoring is not started; the journal is filled directly.</summary>
public sealed class DashboardApiTests : IAsyncDisposable
{
    private readonly FakeTimeProvider _time = new(TestData.Now);
    private readonly WebApplication _app;
    private HttpClient? _client;

    public DashboardApiTests()
    {
        _app = StockQuoteAlertApp.Create(TestData.PetrAndVale, builder =>
        {
            builder.Configuration.AddInMemoryCollection(AppSettings.Valid);
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton<TimeProvider>(_time);

            // The API only reads the journal, so monitoring does not run in these tests.
            WithoutMonitoring(builder.Services);
        });
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IMonitoringJournal Journal => _app.Services.GetRequiredService<IMonitoringJournal>();

    private HttpClient Client => _client ?? throw new InvalidOperationException("Call StartAsync first.");

    [Fact]
    public async Task Health_ReturnsHealthy()
    {
        await StartAsync();

        using var response = await Client.GetAsync(new Uri("/api/health", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("""{"status":"healthy"}""", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Dashboard_WithoutHistory_ShowsTheConfiguration()
    {
        await StartAsync();

        using var document = await GetDashboardAsync();
        var root = document.RootElement;
        var configuration = root.GetProperty("configuration");

        Assert.Equal(
            """[{"ticker":"PETR4","sellAbove":22.67,"buyBelow":22.59},{"ticker":"VALE3","sellAbove":61.50,"buyBelow":60.80}]""",
            configuration.GetProperty("targets").GetRawText());
        Assert.Equal("Yahoo Finance", configuration.GetProperty("quoteProvider").GetString());
        Assert.Equal("brapi.dev", configuration.GetProperty("fallbackQuoteProvider").GetString());
        Assert.Equal(30, configuration.GetProperty("pollingIntervalSeconds").GetDouble());
        Assert.Equal("dest@gmail.com", configuration.GetProperty("recipient").GetString());
        Assert.Equal(TestData.Now, configuration.GetProperty("startedAt").GetDateTimeOffset());
        Assert.Equal(0, root.GetProperty("quotes").GetArrayLength());
        Assert.Equal(0, root.GetProperty("notifications").GetArrayLength());
        Assert.Equal(0, root.GetProperty("lastFailures").GetArrayLength());
    }

    [Fact]
    public async Task Dashboard_StartedAt_IsWhenTheAppWasBuiltNotWhenFirstAsked()
    {
        _time.Advance(TimeSpan.FromMinutes(10));
        await StartAsync();

        using var document = await GetDashboardAsync();

        Assert.Equal(TestData.Now, document.RootElement.GetProperty("configuration").GetProperty("startedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Dashboard_ListsQuotesNotificationsAndFailuresWithEnumsAsText()
    {
        await StartAsync();
        var alert = TestData.SellAlert(22.70m);
        var notificationId = Guid.CreateVersion7();
        Journal.RecordQuote(new QuoteObservation(TestData.QuoteAt(22.70m), PriceZone.AboveSellThreshold, TestData.Now.AddSeconds(1)));
        Journal.RecordNotification(new NotificationRecord(notificationId, alert, "E-mail", "dest@gmail.com", "[VENDA] PETR4", "Olá", TestData.Now.AddSeconds(2), NotificationStatus.Failed, "Connection refused"));
        Journal.RecordFailure(new MonitoringFailure(TestData.Vale3, TestData.Now.AddSeconds(3), "HTTP 503", MonitoringFailureKind.Transient));

        using var document = await GetDashboardAsync();
        var root = document.RootElement;

        var quote = Assert.Single(root.GetProperty("quotes").EnumerateArray());
        Assert.Equal("PETR4", quote.GetProperty("ticker").GetString());
        Assert.Equal(22.70m, quote.GetProperty("price").GetDecimal());
        Assert.Equal("AboveSellThreshold", quote.GetProperty("zone").GetString());
        Assert.Equal("Yahoo Finance", quote.GetProperty("source").GetString());
        Assert.Equal(TestData.Now, quote.GetProperty("quotedAt").GetDateTimeOffset());
        Assert.Equal(TestData.Now.AddSeconds(1), quote.GetProperty("observedAt").GetDateTimeOffset());

        var notification = Assert.Single(root.GetProperty("notifications").EnumerateArray());
        Assert.Equal(notificationId, notification.GetProperty("id").GetGuid());
        Assert.Equal("PETR4", notification.GetProperty("ticker").GetString());
        Assert.Equal("Sell", notification.GetProperty("action").GetString());
        Assert.Equal(22.70m, notification.GetProperty("price").GetDecimal());
        Assert.Equal(22.67m, notification.GetProperty("referencePrice").GetDecimal());
        Assert.Equal("E-mail", notification.GetProperty("channel").GetString());
        Assert.Equal("[VENDA] PETR4", notification.GetProperty("subject").GetString());
        Assert.Equal("Olá", notification.GetProperty("body").GetString());
        Assert.Equal("Failed", notification.GetProperty("status").GetString());
        Assert.Equal("Connection refused", notification.GetProperty("error").GetString());

        var failure = Assert.Single(root.GetProperty("lastFailures").EnumerateArray());
        Assert.Equal("VALE3", failure.GetProperty("ticker").GetString());
        Assert.Equal("HTTP 503", failure.GetProperty("reason").GetString());
        Assert.Equal("Transient", failure.GetProperty("kind").GetString());
        Assert.True(failure.GetProperty("isRecoverable").GetBoolean());
    }

    [Fact]
    public async Task Dashboard_WithoutFallback_ReportsNull()
    {
        await using var app = StockQuoteAlertApp.Create(TestData.Targets, builder =>
        {
            builder.Configuration.AddInMemoryCollection(AppSettings.WithoutFallback);
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            WithoutMonitoring(builder.Services);
        });
        await app.StartAsync(Ct);

        var snapshot = await app.GetTestClient().GetFromJsonAsync<JsonElement>(new Uri("/api/dashboard", UriKind.Relative), Ct);

        Assert.Equal(JsonValueKind.Null, snapshot.GetProperty("configuration").GetProperty("fallbackQuoteProvider").ValueKind);
    }

    [Theory]
    [InlineData("/api/unknown")]
    [InlineData("/")]
    [InlineData("/dashboard")]
    public async Task UnknownRoutes_ReturnNotFound(string path)
    {
        await StartAsync();

        using var response = await Client.GetAsync(new Uri(path, UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/health")]
    [InlineData("/api/dashboard")]
    public async Task OtherMethods_AreNotAllowed(string path)
    {
        await StartAsync();

        using var response = await Client.PostAsync(new Uri(path, UriKind.Relative), content: null, Ct);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task RealServer_ServesTheApiOnTheConfiguredUrl()
    {
        // A real Kestrel server on a free local port; the shipped default is http://localhost:5080.
        await using var app = StockQuoteAlertApp.Create(TestData.Targets, builder =>
        {
            builder.Configuration.AddInMemoryCollection(AppSettings.WithoutFallback);
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Urls"] = "http://127.0.0.1:0" });
            builder.Logging.ClearProviders();
            WithoutMonitoring(builder.Services);
        });
        await app.StartAsync(Ct);

        var address = Assert.Single(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses);
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        var health = await client.GetFromJsonAsync<JsonElement>(new Uri("/api/health", UriKind.Relative), Ct);
        await app.StopAsync(Ct);

        Assert.StartsWith("http://127.0.0.1:", address, StringComparison.Ordinal);
        Assert.Equal("healthy", health.GetProperty("status").GetString());
    }

    public async ValueTask DisposeAsync()
    {
        _client?.Dispose();
        await _app.DisposeAsync();
    }

    /// <summary>TestServer hands out clients only once the app is running.</summary>
    private async Task StartAsync()
    {
        await _app.StartAsync(Ct);
        _client = _app.GetTestClient();
    }

    private async Task<JsonDocument> GetDashboardAsync()
    {
        using var response = await Client.GetAsync(new Uri("/api/dashboard", UriKind.Relative), Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(Ct), cancellationToken: Ct);
    }

    /// <summary>Removes only the monitoring worker; the web server is a hosted service too and must stay.</summary>
    private static void WithoutMonitoring(IServiceCollection services)
    {
        var worker = services.Single(service => service.ImplementationType == typeof(MonitoringWorker));
        services.Remove(worker);
    }
}