using StockQuoteAlert.Core.Application.Monitoring;
using StockQuoteAlert.Core.Domain;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Core.Application.Monitoring;

public sealed class MonitoringTargetTests
{
    [Fact]
    public void Constructor_ExposesTickerAndThresholds()
    {
        var target = new MonitoringTarget(TestData.Petr4, TestData.Thresholds);

        Assert.Equal(TestData.Petr4, target.Ticker);
        Assert.Equal(TestData.Thresholds, target.Thresholds);
    }

    [Fact]
    public void Targets_WithSameValues_AreEqual()
    {
        Assert.Equal(
            new MonitoringTarget(Ticker.Create("petr4"), new AlertThresholds(22.67m, 22.59m)),
            new MonitoringTarget(Ticker.Create("PETR4"), new AlertThresholds(22.67m, 22.59m)));
    }

    [Fact]
    public void Targets_WithDifferentValues_AreNotEqual()
    {
        Assert.NotEqual(TestData.Target, TestData.Target with { Ticker = Ticker.Create("VALE3") });
        Assert.NotEqual(TestData.Target, TestData.Target with { Thresholds = new AlertThresholds(30m, 20m) });
    }
}