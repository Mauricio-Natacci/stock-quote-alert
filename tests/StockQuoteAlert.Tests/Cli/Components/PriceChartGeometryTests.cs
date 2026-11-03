using StockQuoteAlert.Cli.Components.Dashboard;
using StockQuoteAlert.Core.Application.Journal;
using StockQuoteAlert.Core.Domain;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Cli.Components;

public sealed class PriceChartGeometryTests
{
    [Fact]
    public void Create_WithoutQuotes_ReturnsNull()
    {
        Assert.Null(PriceChartGeometry.Create([], TestData.Thresholds));
    }

    [Fact]
    public void Create_WithNullQuotes_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => PriceChartGeometry.Create(null!, TestData.Thresholds));
    }

    [Fact]
    public void PricesAndReferences_FitInsideThePlotWithSellAboveBuy()
    {
        var chart = Chart(22.50m, 22.63m, 22.80m);

        Assert.True(chart.SellY < chart.BuyY);
        Assert.All(chart.Points, point => Assert.InRange(point.Y, PriceChartGeometry.Top, PriceChartGeometry.Bottom));
        Assert.True(chart.Points[2].Y < chart.SellY);
        Assert.True(chart.Points[0].Y > chart.BuyY);
        Assert.InRange(chart.Points[1].Y, chart.SellY, chart.BuyY);
    }

    [Fact]
    public void ReferencesFarFromThePrices_StayVisible()
    {
        var chart = PriceChartGeometry.Create([Observation(22.63m, 0)], new AlertThresholds(100m, 1m))!;

        Assert.InRange(chart.SellY, PriceChartGeometry.Top, PriceChartGeometry.Bottom);
        Assert.InRange(chart.BuyY, PriceChartGeometry.Top, PriceChartGeometry.Bottom);
    }

    [Fact]
    public void ReferencesACentApart_StillGetAVisibleMargin()
    {
        // With prices and references within a few cents, 15% of the range would be almost nothing; a cent is the floor.
        var chart = PriceChartGeometry.Create([Observation(22.635m, 0)], new AlertThresholds(22.64m, 22.63m))!;

        var ticks = chart.YTicks().ToList();
        Assert.Equal("R$ 22,62", ticks[0].Label);
        Assert.Equal("R$ 22,65", ticks[^1].Label);
        Assert.InRange(chart.SellY, PriceChartGeometry.Top + 1, PriceChartGeometry.Bottom - 1);
        Assert.InRange(chart.BuyY, PriceChartGeometry.Top + 1, PriceChartGeometry.Bottom - 1);
    }

    [Fact]
    public void Points_SpanThePlotWidthInTimeOrder()
    {
        var chart = Chart(22.60m, 22.61m, 22.62m);

        Assert.Equal(PriceChartGeometry.Left, chart.Points[0].X);
        Assert.Equal(PriceChartGeometry.Right, chart.Points[^1].X);
        Assert.Equal((PriceChartGeometry.Left + PriceChartGeometry.Right) / 2, chart.Points[1].X, precision: 6);
    }

    [Fact]
    public void Points_ArePlacedByTimeNotByIndex()
    {
        var chart = PriceChartGeometry.Create([Observation(22.60m, 0), Observation(22.61m, 10), Observation(22.62m, 40)], TestData.Thresholds)!;

        var quarter = PriceChartGeometry.Left + ((PriceChartGeometry.Right - PriceChartGeometry.Left) / 4);
        Assert.Equal(quarter, chart.Points[1].X, precision: 6);
    }

    [Fact]
    public void SinglePoint_IsCentered()
    {
        var chart = Chart(22.63m);

        Assert.Equal((PriceChartGeometry.Left + PriceChartGeometry.Right) / 2, Assert.Single(chart.Points).X);
    }

    [Fact]
    public void ManyQuotes_OnlyTheLatestAreDrawn()
    {
        var quotes = Enumerable.Range(0, PriceChartGeometry.MaxPoints + 20).Select(i => Observation(22.60m, i)).ToList();

        var chart = PriceChartGeometry.Create(quotes, TestData.Thresholds)!;

        Assert.Equal(PriceChartGeometry.MaxPoints, chart.Points.Count);
        Assert.Equal(quotes[20], chart.Points[0].Observation);
    }

    [Fact]
    public void LinePath_MovesToTheFirstPointAndDrawsToTheOthers()
    {
        var chart = Chart(22.60m, 22.61m);

        var path = chart.LinePath();

        Assert.StartsWith($"M{PriceChartGeometry.Svg(chart.Points[0].X)},{PriceChartGeometry.Svg(chart.Points[0].Y)} L", path, StringComparison.Ordinal);
        Assert.Equal(1, path.Count(character => character == 'L'));
    }

    [Fact]
    public void YTicks_AreFiveEvenlySpacedPriceLabels()
    {
        var ticks = Chart(22.50m, 22.80m).YTicks().ToList();

        Assert.Equal(5, ticks.Count);
        Assert.Equal(PriceChartGeometry.Bottom, ticks[0].Position, precision: 6);
        Assert.Equal(PriceChartGeometry.Top, ticks[^1].Position, precision: 6);
        Assert.All(ticks, tick => Assert.StartsWith("R$ ", tick.Label, StringComparison.Ordinal));
    }

    [Fact]
    public void XTicks_AreAtMostSixTimeLabelsInBrasiliaTime()
    {
        var chart = PriceChartGeometry.Create([.. Enumerable.Range(0, 20).Select(i => Observation(22.60m, i * 30))], TestData.Thresholds)!;

        var ticks = chart.XTicks().ToList();

        Assert.InRange(ticks.Count, 1, 6);
        Assert.Equal("14:30:00", ticks[0].Label);
        Assert.Equal(chart.Points[0].X, ticks[0].Position);
    }

    [Fact]
    public void HitAreas_CoverThePlotWithoutGapsOrOverlaps()
    {
        var chart = Chart(22.60m, 22.61m, 22.62m, 22.63m);

        var areas = Enumerable.Range(0, chart.Points.Count).Select(chart.HitArea).ToList();

        Assert.Equal(PriceChartGeometry.Left, areas[0].Start);
        Assert.Equal(PriceChartGeometry.Right, areas[^1].Start + areas[^1].Width, precision: 6);
        for (var i = 1; i < areas.Count; i++)
        {
            Assert.Equal(areas[i - 1].Start + areas[i - 1].Width, areas[i].Start, precision: 6);
        }
    }

    [Fact]
    public void HitArea_IsNeverZeroWide()
    {
        var chart = PriceChartGeometry.Create([Observation(22.60m, 0), Observation(22.61m, 0)], TestData.Thresholds)!;

        Assert.All(Enumerable.Range(0, 2), index => Assert.True(chart.HitArea(index).Width >= 1));
    }

    [Fact]
    public void AlertMarkers_ShowOnlySentAlertsInsideTheDrawnTimeRange()
    {
        var chart = PriceChartGeometry.Create([Observation(22.60m, 10), Observation(22.70m, 40)], TestData.Thresholds)!;
        NotificationRecord[] notifications =
        [
            Notification(TestData.SellAlert(22.70m), NotificationStatus.Sent, 40),
            Notification(TestData.SellAlert(22.70m), NotificationStatus.Failed, 40),
            Notification(TestData.BuyAlert(22.50m), NotificationStatus.Sent, 0),
            Notification(TestData.BuyAlert(22.50m), NotificationStatus.Sent, 100),
        ];

        var marker = Assert.Single(chart.AlertMarkers(notifications));

        Assert.Equal(AlertKind.Sell, marker.Kind);
        Assert.Equal(chart.Points[1].X, marker.X);
        Assert.Equal(chart.Points[1].Y, marker.Y, precision: 6);
    }

    [Fact]
    public void AlertMarker_SentJustAfterTheLastQuote_IsDrawnOnIt()
    {
        var chart = PriceChartGeometry.Create([Observation(22.60m, 0), Observation(22.70m, 30)], TestData.Thresholds)!;

        var marker = Assert.Single(chart.AlertMarkers([Notification(TestData.SellAlert(22.70m), NotificationStatus.Sent, 30.5)]));

        Assert.Equal(PriceChartGeometry.Right, marker.X);
    }

    [Theory]
    [InlineData(12.345, "12.35")]
    [InlineData(10, "10")]
    [InlineData(0.5, "0.5")]
    public void Svg_UsesADotAndAtMostTwoDecimals(double value, string expected)
    {
        Assert.Equal(expected, PriceChartGeometry.Svg(value));
    }

    [Fact]
    public void Percent_IsRelativeToTheTotal()
    {
        Assert.Equal("25", PriceChartGeometry.Percent(250, 1000));
    }

    private static PriceChartGeometry Chart(params decimal[] prices) =>
        PriceChartGeometry.Create([.. prices.Select((price, index) => Observation(price, index * 30))], TestData.Thresholds)!;

    private static QuoteObservation Observation(decimal price, double seconds) =>
        new(new Quote(TestData.Petr4, price, TestData.Now, "Yahoo Finance"), TestData.Thresholds.Classify(price), TestData.Now.AddSeconds(seconds));

    private static NotificationRecord Notification(PriceAlert alert, NotificationStatus status, double seconds) =>
        new(Guid.CreateVersion7(), alert, "E-mail", "dest@gmail.com", "subject", "body", TestData.Now.AddSeconds(seconds), status);
}