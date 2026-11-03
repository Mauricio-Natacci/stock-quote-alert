using StockQuoteAlert.Core.Domain;

namespace StockQuoteAlert.Cli.Components.Dashboard;

internal sealed record AlertMarker(double X, double Y, AlertKind Kind);