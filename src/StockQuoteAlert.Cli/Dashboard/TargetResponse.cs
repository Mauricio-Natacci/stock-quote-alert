using StockQuoteAlert.Core.Application.Monitoring;

namespace StockQuoteAlert.Cli.Dashboard;

internal sealed record TargetResponse(string Ticker, decimal SellAbove, decimal BuyBelow)
{
    public static TargetResponse From(MonitoringTarget target) =>
        new(target.Ticker.Symbol, target.Thresholds.SellAbove, target.Thresholds.BuyBelow);
}