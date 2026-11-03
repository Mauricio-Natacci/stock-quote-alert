using StockQuoteAlert.Core.Application.Monitoring;
using StockQuoteAlert.Core.Domain;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Core.Application.Monitoring;

public sealed class MonitoringTargetsTests
{
    [Fact]
    public void KeepsTargetsInTheGivenOrder()
    {
        var targets = new MonitoringTargets([TestData.ValeTarget, TestData.Target]);

        Assert.Equal(2, targets.Count);
        Assert.Equal(TestData.ValeTarget, targets[0]);
        Assert.Equal(TestData.Target, targets[1]);
        Assert.Equal([TestData.ValeTarget, TestData.Target], targets);
    }

    [Fact]
    public void SingleTarget_IsAllowed()
    {
        Assert.Equal(TestData.Target, Assert.Single(new MonitoringTargets([TestData.Target])));
    }

    [Fact]
    public void CopiesTheGivenSequence()
    {
        var source = new List<MonitoringTarget> { TestData.Target };
        var targets = new MonitoringTargets(source);

        source.Add(TestData.ValeTarget);

        Assert.Single(targets);
    }

    [Fact]
    public void CanBeEnumeratedNonGenerically()
    {
        var targets = new MonitoringTargets([TestData.Target, TestData.ValeTarget]);

        Assert.Equal([TestData.Target, TestData.ValeTarget], ((System.Collections.IEnumerable)targets).Cast<MonitoringTarget>());
    }

    [Fact]
    public void IndexOutOfRange_Throws()
    {
        var targets = new MonitoringTargets([TestData.Target]);

        Assert.Throws<IndexOutOfRangeException>(() => targets[1]);
    }

    [Fact]
    public void WithoutTargets_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => new MonitoringTargets([]));

        Assert.StartsWith("At least one stock must be monitored.", exception.Message, StringComparison.Ordinal);
        Assert.Equal("targets", exception.ParamName);
    }

    [Fact]
    public void WithRepeatedTicker_Throws()
    {
        var repeated = new MonitoringTarget(TestData.Petr4, new AlertThresholds(30m, 20m));

        var exception = Assert.Throws<ArgumentException>(() => new MonitoringTargets([TestData.Target, TestData.ValeTarget, repeated]));

        Assert.StartsWith("PETR4 is monitored more than once.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WithNullTarget_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => new MonitoringTargets([TestData.Target, null!]));

        Assert.StartsWith("A monitored stock cannot be null.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WithNullSequence_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new MonitoringTargets(null!));
    }
}