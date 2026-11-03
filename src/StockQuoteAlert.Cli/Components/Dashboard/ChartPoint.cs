using StockQuoteAlert.Core.Application.Journal;

namespace StockQuoteAlert.Cli.Components.Dashboard;

internal sealed record ChartPoint(double X, double Y, QuoteObservation Observation);