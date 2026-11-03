using System.Globalization;
using StockQuoteAlert.Core.Application.Journal;
using StockQuoteAlert.Core.Domain;
using StockQuoteAlert.Core.Formatting;

namespace StockQuoteAlert.Cli.Components.Dashboard;

/// <summary>Maps quotes and thresholds onto the SVG coordinate space of the price chart.</summary>
internal sealed class PriceChartGeometry
{
    public const double Width = 1000;
    public const double Height = 320;
    public const int MaxPoints = 300;

    private const double PlotLeft = 88;
    private const double PlotRight = Width - 16;
    private const double PlotTop = 16;
    private const double PlotBottom = Height - 32;
    private const double PaddingRatio = 0.15;
    private const decimal MinimumPadding = 0.01m;
    private const int YTickCount = 5;
    private const int MaxXTicks = 6;

    private readonly decimal _minPrice;
    private readonly decimal _maxPrice;
    private readonly DateTimeOffset _start;
    private readonly DateTimeOffset _end;

    private PriceChartGeometry(IReadOnlyList<QuoteObservation> quotes, AlertThresholds thresholds)
    {
        var prices = quotes.Select(quote => quote.Quote.Price).Append(thresholds.SellAbove).Append(thresholds.BuyBelow).ToList();
        var padding = Math.Max((prices.Max() - prices.Min()) * (decimal)PaddingRatio, MinimumPadding);

        _minPrice = prices.Min() - padding;
        _maxPrice = prices.Max() + padding;
        _start = quotes[0].ObservedAt;
        _end = quotes[^1].ObservedAt;

        Points = [.. quotes.Select(quote => new ChartPoint(X(quote.ObservedAt), Y(quote.Quote.Price), quote))];
        SellY = Y(thresholds.SellAbove);
        BuyY = Y(thresholds.BuyBelow);
    }

    public IReadOnlyList<ChartPoint> Points { get; }

    public double SellY { get; }

    public double BuyY { get; }

    public static double Left => PlotLeft;

    public static double Right => PlotRight;

    public static double Top => PlotTop;

    public static double Bottom => PlotBottom;

    public static PriceChartGeometry? Create(IReadOnlyList<QuoteObservation> quotes, AlertThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(quotes);

        return quotes.Count == 0 ? null : new PriceChartGeometry([.. quotes.TakeLast(MaxPoints)], thresholds);
    }

    public string LinePath() =>
        string.Join(' ', Points.Select((point, index) => $"{(index == 0 ? 'M' : 'L')}{Svg(point.X)},{Svg(point.Y)}"));

    public IEnumerable<ChartTick> YTicks() =>
        Enumerable.Range(0, YTickCount).Select(i =>
        {
            var price = _minPrice + ((_maxPrice - _minPrice) * i / (YTickCount - 1));
            return new ChartTick(Y(price), BrazilianFormat.Currency(price));
        });

    public IEnumerable<ChartTick> XTicks()
    {
        var step = Math.Max(1, (int)Math.Ceiling(Points.Count / (double)MaxXTicks));

        return Points
            .Where((_, index) => index % step == 0)
            .Select(point => new ChartTick(point.X, BrazilianFormat.Time(point.Observation.ObservedAt)));
    }

    /// <summary>Hit area of each point: from halfway to the previous point to halfway to the next one.</summary>
    public (double Start, double Width) HitArea(int index)
    {
        var start = index == 0 ? PlotLeft : (Points[index - 1].X + Points[index].X) / 2;
        var end = index == Points.Count - 1 ? PlotRight : (Points[index].X + Points[index + 1].X) / 2;
        return (start, Math.Max(end - start, 1));
    }

    public IEnumerable<AlertMarker> AlertMarkers(IEnumerable<NotificationRecord> notifications) =>
        notifications
            .Where(notification => notification.Status == NotificationStatus.Sent
                && notification.AttemptedAt >= _start
                && notification.AttemptedAt <= _end.AddSeconds(1))
            .Select(notification => new AlertMarker(
                X(Clamp(notification.AttemptedAt)),
                Y(notification.Alert.CurrentPrice),
                notification.Alert.Kind));

    public static string Svg(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    public static string Percent(double value, double total) => Svg(value / total * 100);

    private DateTimeOffset Clamp(DateTimeOffset value) => value > _end ? _end : value;

    private double X(DateTimeOffset time)
    {
        var span = (_end - _start).TotalMilliseconds;

        return span <= 0
            ? (PlotLeft + PlotRight) / 2
            : PlotLeft + ((time - _start).TotalMilliseconds / span * (PlotRight - PlotLeft));
    }

    private double Y(decimal price) =>
        PlotBottom - (double)((price - _minPrice) / (_maxPrice - _minPrice)) * (PlotBottom - PlotTop);
}