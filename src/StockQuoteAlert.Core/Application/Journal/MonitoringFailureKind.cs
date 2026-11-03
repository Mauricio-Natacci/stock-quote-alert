namespace StockQuoteAlert.Core.Application.Journal;

public enum MonitoringFailureKind
{
    /// <summary>Temporary problem (network, provider outage); the stock is checked again on the next tick.</summary>
    Transient,

    /// <summary>No provider knows the ticker; the stock is no longer checked.</summary>
    TickerNotFound,

    /// <summary>A provider refused the request for good (e.g. rejected token); the stock is no longer checked.</summary>
    Rejected,
}