namespace StockQuoteAlert.Core.Application.Monitoring;

/// <summary>How monitoring ended, read by the CLI after the host stops to choose the exit code.</summary>
public sealed class MonitoringOutcome
{
    private volatile bool _stoppedByUnrecoverableFailure;

    /// <summary>
    /// <see langword="true"/> when monitoring stopped because of an unrecoverable failure (unknown ticker, rejected
    /// request); <see langword="false"/> when the user stopped it.
    /// </summary>
    public bool StoppedByUnrecoverableFailure => _stoppedByUnrecoverableFailure;

    internal void MarkUnrecoverableFailure() => _stoppedByUnrecoverableFailure = true;
}