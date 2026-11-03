using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StockQuoteAlert.Cli.Logging;
using StockQuoteAlert.Core.Application.Monitoring;

namespace StockQuoteAlert.Cli;

internal static class StockQuoteAlertApp
{
    /// <param name="configure">Lets tests replace configuration and external boundaries before the host is built.</param>
    public static IHost Create(MonitoringTargets targets, Action<HostApplicationBuilder>? configure = null)
    {
        var builder = Host.CreateApplicationBuilder(BuilderSettings());

        // Selected through Logging:Console:FormatterName in appsettings.json.
        builder.Logging.AddConsoleFormatter<CompactConsoleFormatter, CompactConsoleFormatterOptions>();

        builder.Services.AddStockQuoteAlert(targets);
        configure?.Invoke(builder);

        return builder.Build();
    }

    /// <summary>
    /// Configuration comes from appsettings.json next to the executable (whatever the working directory),
    /// user-secrets in Development (found through the assembly's UserSecretsId), then environment variables.
    /// </summary>
    internal static HostApplicationBuilderSettings BuilderSettings() => new()
    {
        ApplicationName = typeof(StockQuoteAlertApp).Assembly.GetName().Name,
        ContentRootPath = AppContext.BaseDirectory,
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