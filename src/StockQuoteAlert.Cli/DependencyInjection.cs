using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StockQuoteAlert.Core.Application.Monitoring;
using StockQuoteAlert.Infrastructure;

namespace StockQuoteAlert.Cli;

internal static class DependencyInjection
{
    public static IServiceCollection AddStockQuoteAlert(this IServiceCollection services, MonitoringTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(target);
        services.AddSingleton<MonitoringOutcome>();
        services.AddValidatedOptions<MonitoringOptions>(MonitoringOptions.SectionName);

        services.AddSingleton<StockMonitor>();
        services.AddHostedService<MonitoringWorker>();

        return services.AddInfrastructure();
    }
}