using StockQuoteAlert.Core.Application.Monitoring;

namespace StockQuoteAlert.Tests.Core.Application.Monitoring;

public sealed class MonitoringOutcomeTests
{
    [Fact]
    public void StartsWithoutFailure()
    {
        Assert.False(new MonitoringOutcome().StoppedByUnrecoverableFailure);
    }

    [Fact]
    public void MarkUnrecoverableFailure_IsRemembered()
    {
        var outcome = new MonitoringOutcome();

        outcome.MarkUnrecoverableFailure();
        outcome.MarkUnrecoverableFailure();

        Assert.True(outcome.StoppedByUnrecoverableFailure);
    }
}