using StockQuoteAlert.Core.Application.Monitoring;

namespace StockQuoteAlert.Tests.Core.Application.Monitoring;

public sealed class MonitoringOutcomeTests
{
    [Fact]
    public void StartsWithoutFailure()
    {
        Assert.False(new MonitoringOutcome().NothingLeftToMonitor);
    }

    [Fact]
    public void MarkNothingLeftToMonitor_IsRemembered()
    {
        var outcome = new MonitoringOutcome();

        outcome.MarkNothingLeftToMonitor();
        outcome.MarkNothingLeftToMonitor();

        Assert.True(outcome.NothingLeftToMonitor);
    }
}