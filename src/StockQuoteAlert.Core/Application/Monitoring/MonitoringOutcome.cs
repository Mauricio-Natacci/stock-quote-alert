namespace StockQuoteAlert.Core.Application.Monitoring;

/// <summary>How monitoring ended, read by the CLI after the host stops to choose the exit code.</summary>
public sealed class MonitoringOutcome
{
    private volatile bool _nothingLeftToMonitor;

    /// <summary>
    /// <see langword="true"/> when every stock hit an unrecoverable failure (unknown ticker, rejected request) and
    /// monitoring stopped on its own; <see langword="false"/> when the user stopped it.
    /// </summary>
    public bool NothingLeftToMonitor => _nothingLeftToMonitor;

    internal void MarkNothingLeftToMonitor() => _nothingLeftToMonitor = true;
}