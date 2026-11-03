using StockQuoteAlert.Core.Domain;

namespace StockQuoteAlert.Core.Application.Monitoring;

/// <summary>The asset to watch and its sell and buy references, as given on the command line.</summary>
public sealed record MonitoringTarget(Ticker Ticker, AlertThresholds Thresholds);