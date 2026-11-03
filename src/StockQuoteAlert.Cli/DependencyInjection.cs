using Microsoft.Extensions.DependencyInjection.Extensions;
using StockQuoteAlert.Core.Application.Monitoring;
using StockQuoteAlert.Infrastructure;

namespace StockQuoteAlert.Cli;

internal static class DependencyInjection
{
    public static IServiceCollection AddStockQuoteAlert(this IServiceCollection services, MonitoringTargets targets)
    {
        ArgumentNullException.ThrowIfNull(targets);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(targets);
        services.AddSingleton<MonitoringOutcome>();
        services.AddValidatedOptions<MonitoringOptions>(MonitoringOptions.SectionName);

        // One monitor per stock: each keeps its own alert zone, so alerts (and e-mails) never mix stocks.
        foreach (var target in targets)
        {
            services.AddSingleton(provider => ActivatorUtilities.CreateInstance<StockMonitor>(provider, target));
        }

        services.AddHostedService<MonitoringWorker>();

        return services.AddInfrastructure();
    }
}