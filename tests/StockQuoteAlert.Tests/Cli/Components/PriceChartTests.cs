using Bunit;
using StockQuoteAlert.Cli.Components.Dashboard;
using StockQuoteAlert.Core.Application.Journal;
using StockQuoteAlert.Core.Domain;
using StockQuoteAlert.Core.Formatting;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Cli.Components;

public sealed class PriceChartTests : DashboardTestContext
{
    [Fact]
    public void WithoutQuotes_SaysItIsWaiting()
    {
        var chart = RenderChart([]);

        Assert.Contains("Aguardando a primeira cotação…", chart.Markup, StringComparison.Ordinal);
        Assert.Empty(chart.FindAll("svg"));
    }

    [Fact]
    public void Title_LegendAndThresholdLabels_NameTheStockAndReferences()
    {
        var chart = RenderChart([Observation(22.63m, 0)]);

        Assert.Equal("Cotação de PETR4", chart.Find("#chart-title").TextContent);
        Assert.Contains("Linha de venda: alerta acima de R$ 22,67", chart.Find(".legend").TextContent, StringComparison.Ordinal);
        Assert.Contains("Linha de compra: alerta abaixo de R$ 22,59", chart.Find(".legend").TextContent, StringComparison.Ordinal);
        Assert.Equal("Linha de venda · R$ 22,67", chart.Find(".chart__threshold-label").TextContent);
        Assert.Equal("Gráfico de preço de PETR4", chart.Find("svg").GetAttribute("aria-label"));
    }

    [Fact]
    public void Chart_DrawsTheNeutralBandBothLinesAndThePrice()
    {
        var chart = RenderChart([Observation(22.60m, 0), Observation(22.63m, 30)]);

        Assert.Single(chart.FindAll(".chart__band"));
        Assert.Single(chart.FindAll(".chart__threshold--sell"));
        Assert.Single(chart.FindAll(".chart__threshold--buy"));
        Assert.StartsWith("M", chart.Find(".chart__line").GetAttribute("d"), StringComparison.Ordinal);
        Assert.Equal(2, chart.FindAll("[data-testid=chart-hit]").Count);
    }

    [Fact]
    public void Chart_MarksSentAlertsByKind()
    {
        var chart = RenderChart(
            [Observation(22.60m, 0), Observation(22.70m, 30), Observation(22.50m, 60)],
            [Notification(TestData.SellAlert(22.70m), NotificationStatus.Sent, 30), Notification(TestData.BuyAlert(22.50m), NotificationStatus.Failed, 60)]);

        var marker = Assert.Single(chart.FindAll(".chart__marker"));
        Assert.Contains("chart__marker--Sell", marker.ClassName, StringComparison.Ordinal);
    }

    [Fact]
    public void Hover_ShowsTheTooltipAndLeavingHidesIt()
    {
        var chart = RenderChart([Observation(22.60m, 0), Observation(22.70m, 30)]);

        chart.FindAll("[data-testid=chart-hit]")[1].MouseOver();

        var tooltip = chart.Find("[role=tooltip]");
        Assert.Equal("14:30:30", tooltip.QuerySelector(".tooltip__time")!.TextContent);
        Assert.Equal("R$ 22,70", tooltip.QuerySelector(".tooltip__value")!.TextContent);
        Assert.Contains("Acima da linha de venda", tooltip.TextContent, StringComparison.Ordinal);
        Assert.Contains("R$ 0,03 (0,13%) acima da linha de venda", tooltip.TextContent, StringComparison.Ordinal);
        Assert.Single(chart.FindAll(".chart__crosshair"));

        chart.Find("svg").MouseLeave();

        Assert.Empty(chart.FindAll("[role=tooltip]"));
        Assert.Empty(chart.FindAll(".chart__crosshair"));
    }

    [Fact]
    public void Toggle_SwitchesToTheTableAndBack()
    {
        var chart = RenderChart([Observation(22.60m, 0)]);
        var button = chart.Find(".card__header button");
        Assert.Equal("Ver tabela", button.TextContent.Trim());

        button.Click();

        Assert.Empty(chart.FindAll("svg"));
        Assert.Single(chart.FindAll("table[aria-label='Cotações registradas']"));
        Assert.Equal("Ver gráfico", chart.Find(".card__header button").TextContent.Trim());

        chart.Find(".card__header button").Click();

        Assert.Single(chart.FindAll("svg"));
    }

    [Fact]
    public void Table_WithoutQuotes_SaysSo()
    {
        var chart = RenderChart([]);

        chart.Find(".card__header button").Click();

        Assert.Contains("Nenhuma cotação registrada ainda.", chart.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Table_ShowsTheLatestRowsNewestFirst()
    {
        var quotes = Enumerable.Range(0, QuotesTable.VisibleRows + 10).Select(i => Observation(22.60m + (i * 0.01m), i * 30)).ToList();
        var chart = RenderChart(quotes);

        chart.Find(".card__header button").Click();

        var rows = chart.FindAll("tbody tr");
        Assert.Equal(QuotesTable.VisibleRows, rows.Count);
        Assert.Contains(Brl(quotes[^1].Quote.Price), rows[0].TextContent, StringComparison.Ordinal);
        Assert.Contains(Brl(quotes[10].Quote.Price), rows[^1].TextContent, StringComparison.Ordinal);
        Assert.Contains("Yahoo Finance", rows[0].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangingStock_ClearsTheHoveredPoint()
    {
        var chart = RenderChart([Observation(22.60m, 0), Observation(22.70m, 30)]);
        chart.FindAll("[data-testid=chart-hit]")[1].MouseOver();

        chart.Render(parameters => parameters.Add(p => p.Ticker, new TickerDashboard(TestData.ValeTarget, [ValeObservation(60.96m)], [], null)));

        Assert.Empty(chart.FindAll("[role=tooltip]"));
        Assert.Equal("Cotação de VALE3", chart.Find("#chart-title").TextContent);
    }

    [Fact]
    public void NewQuoteForTheSameStock_KeepsTheHoveredPoint()
    {
        var chart = RenderChart([Observation(22.60m, 0), Observation(22.70m, 30)]);
        chart.FindAll("[data-testid=chart-hit]")[0].MouseOver();

        chart.Render(parameters => parameters.Add(p => p.Ticker, new TickerDashboard(TestData.Target, [Observation(22.60m, 0), Observation(22.70m, 30), Observation(22.71m, 60)], [], null)));

        Assert.Single(chart.FindAll("[role=tooltip]"));
    }

    private IRenderedComponent<PriceChart> RenderChart(IReadOnlyList<QuoteObservation> quotes, IReadOnlyList<NotificationRecord>? notifications = null) =>
        Render<PriceChart>(parameters => parameters.Add(p => p.Ticker, new TickerDashboard(TestData.Target, quotes, notifications ?? [], null)));

    private static string Brl(decimal price) => BrazilianFormat.Currency(price);

    private static QuoteObservation Observation(decimal price, double seconds) =>
        new(new Quote(TestData.Petr4, price, TestData.Now, "Yahoo Finance"), TestData.Thresholds.Classify(price), TestData.Now.AddSeconds(seconds));

    private static QuoteObservation ValeObservation(decimal price) =>
        new(new Quote(TestData.Vale3, price, TestData.Now, "Yahoo Finance"), TestData.ValeThresholds.Classify(price), TestData.Now);

    private static NotificationRecord Notification(PriceAlert alert, NotificationStatus status, double seconds) =>
        new(Guid.CreateVersion7(), alert, "E-mail", "dest@gmail.com", "subject", "body", TestData.Now.AddSeconds(seconds), status);
}