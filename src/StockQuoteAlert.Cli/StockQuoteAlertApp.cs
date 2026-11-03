using System.Text.Json.Serialization;
using StockQuoteAlert.Cli.Components;
using StockQuoteAlert.Cli.Dashboard;
using StockQuoteAlert.Cli.Logging;
using StockQuoteAlert.Core.Application.Monitoring;

namespace StockQuoteAlert.Cli;

internal static class StockQuoteAlertApp
{
    /// <summary>
    /// Builds the web application: monitoring in the background, the Blazor dashboard and the JSON API
    /// (Urls, http://localhost:5080).
    /// </summary>
    /// <param name="configure">Lets tests replace configuration and external boundaries before the app is built.</param>
    public static WebApplication Create(MonitoringTargets targets, Action<WebApplicationBuilder>? configure = null) =>
        Create(BuilderOptions(), targets, configure);

    internal static WebApplication Create(WebApplicationOptions options, MonitoringTargets targets, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(options);

        // Finds wwwroot from the project when running from bin (dotnet run); a no-op on published output.
        builder.WebHost.UseStaticWebAssets();

        // Selected through Logging:Console:FormatterName in appsettings.json.
        builder.Logging.AddConsoleFormatter<CompactConsoleFormatter, CompactConsoleFormatterOptions>();

        builder.Services.AddStockQuoteAlert(targets);
        builder.Services.AddSingleton(provider => new MonitoringSession(provider.GetRequiredService<TimeProvider>().GetUtcNow()));
        builder.Services.ConfigureHttpJsonOptions(json => json.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        configure?.Invoke(builder);

        var app = builder.Build();

        // Taken now, so the dashboard shows when the app started rather than when it was first asked.
        _ = app.Services.GetRequiredService<MonitoringSession>();

        app.UseAntiforgery();
        app.MapStaticAssets();
        app.MapDashboardApi();
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

        return app;
    }

    /// <summary>
    /// Configuration comes from appsettings.json next to the executable (whatever the working directory),
    /// user-secrets in Development (found through the assembly's UserSecretsId), then environment variables.
    /// </summary>
    internal static WebApplicationOptions BuilderOptions(string? environmentName = null) => new()
    {
        ApplicationName = typeof(StockQuoteAlertApp).Assembly.GetName().Name,
        ContentRootPath = AppContext.BaseDirectory,
        EnvironmentName = environmentName,
    };

    /// <summary>Runs until the user stops the app (Ctrl+C) or no stock is left to monitor, and returns the exit code.</summary>
    public static async Task<int> RunAsync(IHost host, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);

        // Resolved before running: once the host stops, its services are disposed.
        var outcome = host.Services.GetRequiredService<MonitoringOutcome>();

        await host.RunAsync(cancellationToken).ConfigureAwait(false);

        return outcome.NothingLeftToMonitor
            ? ExitCodes.UnrecoverableMonitoringFailure
            : ExitCodes.Success;
    }
}