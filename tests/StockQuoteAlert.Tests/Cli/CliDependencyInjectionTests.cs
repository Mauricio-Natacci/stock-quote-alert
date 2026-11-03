using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using StockQuoteAlert.Cli;
using StockQuoteAlert.Core.Application.Abstractions;
using StockQuoteAlert.Core.Application.Monitoring;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Cli;

public sealed class CliDependencyInjectionTests
{
    [Fact]
    public void AddStockQuoteAlert_RegistersTheMonitorForTheTarget()
    {
        using var services = BuildProvider();

        var monitor = services.GetRequiredService<StockMonitor>();

        Assert.Equal(TestData.Target, monitor.Target);
        Assert.Same(monitor, services.GetRequiredService<StockMonitor>());
        Assert.Same(TestData.Target, services.GetRequiredService<MonitoringTarget>());
    }

    [Fact]
    public void AddStockQuoteAlert_RegistersTheWorkerAsHostedService()
    {
        using var services = BuildProvider();

        Assert.IsType<MonitoringWorker>(Assert.Single(services.GetServices<IHostedService>()));
    }

    [Fact]
    public void AddStockQuoteAlert_SharesOneOutcome()
    {
        using var services = BuildProvider();

        Assert.Same(services.GetRequiredService<MonitoringOutcome>(), services.GetRequiredService<MonitoringOutcome>());
    }

    [Fact]
    public void AddStockQuoteAlert_RegistersInfrastructure()
    {
        using var services = BuildProvider();

        Assert.NotNull(services.GetRequiredService<IQuoteProvider>());
        Assert.NotNull(services.GetRequiredService<IAlertNotifier>());
    }

    [Fact]
    public void AddStockQuoteAlert_UsesSystemClockUnlessOneIsRegistered()
    {
        using var defaults = BuildProvider();
        var fakeTime = new FakeTimeProvider();
        using var overridden = BuildProvider(services => services.AddSingleton<TimeProvider>(fakeTime));

        Assert.Same(TimeProvider.System, defaults.GetRequiredService<TimeProvider>());
        Assert.Same(fakeTime, overridden.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public void MonitoringOptions_AreBoundFromConfiguration()
    {
        using var services = BuildProvider(settings: new() { ["Monitoring:PollingInterval"] = "00:01:00" });

        Assert.Equal(TimeSpan.FromMinutes(1), services.GetRequiredService<IOptions<MonitoringOptions>>().Value.PollingInterval);
    }

    [Fact]
    public void MonitoringOptions_AreValidatedOnStart()
    {
        using var services = BuildProvider(settings: new() { ["Monitoring:PollingInterval"] = "00:00:01" });

        var exception = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains(exception.Failures, failure => failure.Contains("Monitoring:PollingInterval must be between 5 seconds and 1 day.", StringComparison.Ordinal));
    }

    [Fact]
    public void AddStockQuoteAlert_WithNullTarget_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddStockQuoteAlert(null!));
    }

    private static ServiceProvider BuildProvider(Action<IServiceCollection>? before = null, Dictionary<string, string?>? settings = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(AppSettings.Valid)
            .AddInMemoryCollection(settings ?? [])
            .Build();

        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton(Substitute.For<IHostApplicationLifetime>())
            .AddLogging();

        before?.Invoke(services);
        services.AddStockQuoteAlert(TestData.Target);

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }
}