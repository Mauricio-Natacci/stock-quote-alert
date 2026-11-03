using Bunit;
using StockQuoteAlert.Cli.Components.Dashboard;
using StockQuoteAlert.Core.Application.Journal;
using StockQuoteAlert.Core.Domain;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Cli.Components;

public sealed class TickersOverviewTests : DashboardTestContext
{
    private readonly List<string> _selections = [];

    [Fact]
    public void ListsEveryStockWithItsPricePositionReferencesEmailsAndStatus()
    {
        var petr = new TickerDashboard(TestData.Target, [Observation(TestData.Petr4, 22.70m, PriceZone.AboveSellThreshold)], [Notification(NotificationStatus.Sent), Notification(NotificationStatus.Failed)], null);
        var vale = new TickerDashboard(TestData.ValeTarget, [Observation(TestData.Vale3, 61.00m, PriceZone.Neutral)], [], null);

        var overview = RenderOverview([petr, vale], "PETR4");

        Assert.Equal("2", overview.Find(".count-pill").TextContent);
        var cells = overview.FindAll("tbody tr")[0].QuerySelectorAll("td").Select(cell => cell.TextContent.Trim()).ToList();
        Assert.Equal(["PETR4", "R$ 22,70", "▲ Acima da linha de venda", "R$ 0,03 (0,13%) acima da linha de venda", "R$ 22,67", "R$ 22,59", "1 · ✕ 1", "Ao vivo"], cells);
        Assert.Contains("0", overview.FindAll("tbody tr")[1].QuerySelectorAll("td")[6].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void StockWithoutQuotes_IsWaitingOrWithoutQuoteWhenStopped()
    {
        var waiting = new TickerDashboard(TestData.Target, [], [], null);
        var stopped = new TickerDashboard(TestData.ValeTarget, [], [], new MonitoringFailure(TestData.Vale3, TestData.Now, "not found", MonitoringFailureKind.TickerNotFound));

        var rows = RenderOverview([waiting, stopped], "PETR4").FindAll("tbody tr");

        Assert.Contains("aguardando a primeira cotação", rows[0].TextContent, StringComparison.Ordinal);
        Assert.Contains("sem cotação", rows[1].TextContent, StringComparison.Ordinal);
        Assert.Contains("Interrompido", rows[1].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectedStock_IsMarked()
    {
        var rows = RenderOverview(TwoStocks(), "VALE3").FindAll("tbody tr");

        Assert.Null(rows[0].GetAttribute("aria-current"));
        Assert.Equal("true", rows[1].GetAttribute("aria-current"));
        Assert.Contains("is-selected", rows[1].ClassName, StringComparison.Ordinal);
    }

    [Fact]
    public void Rows_AreFocusableAndNamed()
    {
        var row = RenderOverview(TwoStocks(), "PETR4").FindAll("tbody tr")[1];

        Assert.Equal("0", row.GetAttribute("tabindex"));
        Assert.Equal("Ver detalhes de VALE3", row.GetAttribute("aria-label"));
    }

    [Fact]
    public void ClickingARow_SelectsThatStock()
    {
        RenderOverview(TwoStocks(), "PETR4").FindAll("tbody tr")[1].Click();

        Assert.Equal(["VALE3"], _selections);
    }

    [Theory]
    [InlineData("Enter")]
    [InlineData(" ")]
    public void EnterOrSpace_SelectsThatStock(string key)
    {
        RenderOverview(TwoStocks(), "PETR4").FindAll("tbody tr")[1].KeyDown(key);

        Assert.Equal(["VALE3"], _selections);
    }

    [Fact]
    public void OtherKeys_SelectNothing()
    {
        RenderOverview(TwoStocks(), "PETR4").FindAll("tbody tr")[1].KeyDown("Tab");

        Assert.Empty(_selections);
    }

    private IRenderedComponent<TickersOverview> RenderOverview(IReadOnlyList<TickerDashboard> tickers, string selected) =>
        Render<TickersOverview>(parameters => parameters
            .Add(p => p.Tickers, tickers)
            .Add(p => p.Selected, selected)
            .Add(p => p.OnSelect, symbol => _selections.Add(symbol)));

    private static TickerDashboard[] TwoStocks() =>
        [new TickerDashboard(TestData.Target, [], [], null), new TickerDashboard(TestData.ValeTarget, [], [], null)];

    private static QuoteObservation Observation(Ticker ticker, decimal price, PriceZone zone) =>
        new(new Quote(ticker, price, TestData.Now, "Yahoo Finance"), zone, TestData.Now);

    private static NotificationRecord Notification(NotificationStatus status) =>
        new(Guid.CreateVersion7(), TestData.SellAlert(), "E-mail", "dest@gmail.com", "subject", "body", TestData.Now, status);
}