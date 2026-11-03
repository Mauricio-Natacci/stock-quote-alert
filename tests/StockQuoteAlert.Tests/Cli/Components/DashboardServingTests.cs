using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StockQuoteAlert.Cli;
using StockQuoteAlert.Core.Application.Monitoring;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Cli.Components;

/// <summary>The real app serves the prerendered dashboard and its static assets; monitoring itself does not run.</summary>
public sealed class DashboardServingTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _app = StockQuoteAlertApp.Create(TestData.Targets, builder =>
        {
            builder.Configuration.AddInMemoryCollection(AppSettings.WithoutFallback);
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Services.Remove(builder.Services.Single(service => service.ImplementationType == typeof(MonitoringWorker)));
        });
        await _app.StartAsync(Ct);
        _client = _app.GetTestClient();
    }

    [Fact]
    public async Task Root_ServesThePrerenderedDashboardInPortuguese()
    {
        using var response = await _client.GetAsync(new Uri("/", UriKind.Relative), Ct);
        var html = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("<html lang=\"pt-BR\">", html, StringComparison.Ordinal);
        Assert.Contains("<title>Stock Quote Alert</title>", html, StringComparison.Ordinal);
        Assert.Contains("Stock Quote Alert</h1>", html, StringComparison.Ordinal);
        Assert.Contains("PETR4", html, StringComparison.Ordinal);
        Assert.Contains("_framework/blazor.web", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("app.css", "--price:")]
    [InlineData("favicon.svg", "<svg")]
    public async Task StaticAssets_AreLinkedAndServed(string asset, string expectedContent)
    {
        var html = await _client.GetStringAsync(new Uri("/", UriKind.Relative), Ct);
        var link = System.Text.RegularExpressions.Regex.Match(html, $"href=\"(?<href>[^\"]*{asset.Split('.')[0]}[^\"]*\\.{asset.Split('.')[1]})\"").Groups["href"].Value;

        using var response = await _client.GetAsync(new Uri("/" + link.TrimStart('/'), UriKind.Relative), Ct);

        Assert.NotEmpty(link);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(expectedContent, await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task JsonApi_IsStillServed()
    {
        using var response = await _client.GetAsync(new Uri("/api/health", UriKind.Relative), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }
}