using Bunit;
using Microsoft.AspNetCore.Components;
using StockQuoteAlert.Cli.Components.Dashboard;
using StockQuoteAlert.Cli.Components.Pages;
using StockQuoteAlert.Core.Application.Journal;
using StockQuoteAlert.Core.Application.Monitoring;
using StockQuoteAlert.Core.Domain;
using StockQuoteAlert.Infrastructure.Quotes;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Cli.Components;

/// <summary>Renders the real dashboard against the real journal; only the clock is faked.</summary>
public sealed class DashboardPageTests : DashboardTestContext
{
    [Fact]
    public void Header_ShowsBrandTickerProviderIntervalAndState()
    {
        var page = Render<DashboardPage>();

        Assert.Equal("Stock Quote Alert", page.Find(".header__title").TextContent);
        Assert.Equal("PETR4", page.Find(".ticker-badge").TextContent);
        Assert.Equal("Fonte: Yahoo Finance · atualiza a cada 30 s", page.Find(".header__detail").TextContent);
        Assert.Contains("Ao vivo", page.Find("[role=status]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Header_WithFallbackAndMinuteInterval_SaysSo()
    {
        QuoteProviderOptions.Fallback = QuoteProviderKind.Brapi;
        MonitoringOptions.PollingInterval = TimeSpan.FromMinutes(2);

        var page = Render<DashboardPage>();

        Assert.Equal("Fonte: Yahoo Finance (reserva: brapi.dev) · atualiza a cada 2 min", page.Find(".header__detail").TextContent);
    }

    [Fact]
    public void Header_WithSeveralStocks_ShowsHowManyAndListsThemOnHover()
    {
        Targets = TestData.PetrAndVale;

        var badge = Render<DashboardPage>().Find(".ticker-badge");

        Assert.Equal("2 ativos", badge.TextContent);
        Assert.Equal("PETR4, VALE3", badge.GetAttribute("title"));
    }

    [Fact]
    public void WithoutQuotes_KpisSayTheFirstCheckIsComing()
    {
        var page = Render<DashboardPage>();

        Assert.Contains("Aguardando…", page.Find(".kpi--hero").TextContent, StringComparison.Ordinal);
        Assert.Contains("a primeira verificação acontece em instantes", page.Markup, StringComparison.Ordinal);
        Assert.Equal("0 enviados", page.Find("[data-testid=emails-sent]").TextContent);
        Assert.Contains("para dest@gmail.com", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Kpis_ShowLastPriceZoneDistanceAndThresholds()
    {
        RecordQuote(22.61m);
        Time.Advance(TimeSpan.FromSeconds(5));

        var page = Render<DashboardPage>();

        Assert.Equal("R$ 22,61", page.Find("[data-testid=last-price]").TextContent);
        Assert.Contains("Na zona neutra", page.Find(".kpi--hero").TextContent, StringComparison.Ordinal);
        Assert.Equal("verificado há 5 s", page.Find("[data-testid=last-check]").TextContent);
        Assert.Equal("faltam R$ 0,02 (0,09%) para a linha de compra", page.Find("[data-testid=distance]").TextContent);
        Assert.Contains("Último preço · PETR4", page.Markup, StringComparison.Ordinal);
        var values = page.FindAll(".kpi__value").Select(value => value.TextContent).ToList();
        Assert.Contains("R$ 22,67", values);
        Assert.Contains("R$ 22,59", values);
    }

    [Fact]
    public void Kpis_ShowTheLatestQuoteWhenThereAreSeveral()
    {
        RecordQuote(22.61m);
        RecordQuote(22.75m, secondsAfterNow: 30);

        var page = Render<DashboardPage>();

        Assert.Equal("R$ 22,75", page.Find("[data-testid=last-price]").TextContent);
        Assert.Contains("Acima da linha de venda", page.Find(".kpi--hero").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Kpis_CountSentAndFailedEmails()
    {
        RecordNotification(TestData.SellAlert());
        RecordNotification(TestData.BuyAlert());
        RecordNotification(TestData.SellAlert(), NotificationStatus.Failed, "Connection refused");

        var page = Render<DashboardPage>();

        Assert.Equal("2 enviados", page.Find("[data-testid=emails-sent]").TextContent);
        Assert.Contains("✕ 1 falhou · para dest@gmail.com", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Kpis_OneSentEmail_IsSingular()
    {
        RecordNotification(TestData.SellAlert());

        Assert.Equal("1 enviado", Render<DashboardPage>().Find("[data-testid=emails-sent]").TextContent);
    }

    [Fact]
    public void StoppedStockWithoutQuotes_SaysMonitoringWasInterrupted()
    {
        RecordFailure(MonitoringFailureKind.TickerNotFound);

        var page = Render<DashboardPage>();

        Assert.Contains("Sem cotação", page.Find(".kpi--hero").TextContent, StringComparison.Ordinal);
        Assert.Contains("o monitoramento deste ativo foi interrompido", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Interrompido", page.Find("[role=status]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void RendersWithoutAnInteractiveCircuit()
    {
        SetRendererInfo(new RendererInfo("Static", isInteractive: false));
        RecordQuote(22.61m);

        Assert.Equal("R$ 22,61", Render<DashboardPage>().Find("[data-testid=last-price]").TextContent);
    }

    [Fact]
    public void NewQuote_UpdatesThePageLive()
    {
        RecordQuote(22.61m);
        var page = Render<DashboardPage>();

        RecordQuote(22.75m, secondsAfterNow: 30);

        page.WaitForAssertion(() =>
        {
            Assert.Equal("R$ 22,75", page.Find("[data-testid=last-price]").TextContent);
            Assert.Contains("Acima da linha de venda", page.Find(".kpi--hero").TextContent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void FirstQuote_ReplacesTheWaitingState()
    {
        var page = Render<DashboardPage>();

        RecordQuote(22.63m);

        page.WaitForAssertion(() => Assert.Equal("R$ 22,63", page.Find("[data-testid=last-price]").TextContent));
    }

    [Fact]
    public void NewNotification_UpdatesTheEmailCountLive()
    {
        var page = Render<DashboardPage>();

        RecordNotification(TestData.SellAlert());

        page.WaitForAssertion(() => Assert.Equal("1 enviado", page.Find("[data-testid=emails-sent]").TextContent));
    }

    [Fact]
    public void NewFailure_UpdatesTheHeaderStateLive()
    {
        RecordQuote(22.63m);
        var page = Render<DashboardPage>();

        RecordFailure(MonitoringFailureKind.Transient, secondsAfterNow: 30);

        page.WaitForAssertion(() => Assert.Contains("Instável", page.Find("[role=status]").TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void EventAboutAnotherStock_RebuildsOnlyThatStock()
    {
        Targets = TestData.PetrAndVale;
        var page = Render<DashboardPage>();
        var petr = page.Instance.Tickers[0];
        var vale = page.Instance.Tickers[1];

        RecordQuote(60.96m, TestData.Vale3);

        page.WaitForAssertion(() => Assert.NotSame(vale, page.Instance.Tickers[1]));
        Assert.Same(petr, page.Instance.Tickers[0]);
        Assert.Single(page.Instance.Tickers[1].Quotes);
    }

    [Fact]
    public async Task EventAboutAStockThatIsNotMonitored_DoesNotRenderAgain()
    {
        var page = Render<DashboardPage>();
        page.WaitForAssertion(() => Assert.Equal(1, Journal.SubscriberCount));
        var header = page.FindComponent<DashboardHeader>();
        var renders = header.RenderCount;

        // Events are handled in order: the VALE3 quote is ignored, the PETR4 quote renders once.
        Journal.RecordQuote(new QuoteObservation(new Quote(TestData.Vale3, 60.96m, TestData.Now, "Yahoo Finance"), PriceZone.Neutral, TestData.Now));
        RecordQuote(22.63m);

        page.WaitForAssertion(() => Assert.Equal("R$ 22,63", page.Find("[data-testid=last-price]").TextContent));
        await Task.Yield();
        Assert.Equal(renders + 1, header.RenderCount);
    }

    [Fact]
    public void LastCheck_TicksWithTheClockWithoutRenderingThePage()
    {
        RecordQuote(22.63m);
        var page = Render<DashboardPage>();
        page.WaitForAssertion(() => Assert.Equal(1, Journal.SubscriberCount));

        // The header re-renders whenever the page does, and has no timer of its own.
        var header = page.FindComponent<DashboardHeader>();
        var renders = header.RenderCount;

        Time.Advance(TimeSpan.FromSeconds(3));
        page.WaitForAssertion(() => Assert.Equal("verificado há 3 s", page.Find("[data-testid=last-check]").TextContent));

        Time.Advance(TimeSpan.FromSeconds(60));
        page.WaitForAssertion(() => Assert.Equal("verificado há 1 min", page.Find("[data-testid=last-check]").TextContent));
        Assert.Equal(renders, header.RenderCount);
    }

    [Fact]
    public void InteractivePage_SubscribesToTheJournal()
    {
        var page = Render<DashboardPage>();

        page.WaitForAssertion(() => Assert.Equal(1, Journal.SubscriberCount));
    }

    [Fact]
    public void PrerenderedPage_DoesNotSubscribe()
    {
        SetRendererInfo(new RendererInfo("Static", isInteractive: false));

        Render<DashboardPage>();

        Assert.Equal(0, Journal.SubscriberCount);
    }

    [Fact]
    public async Task Dispose_StopsListeningToTheJournal()
    {
        var page = Render<DashboardPage>();
        page.WaitForAssertion(() => Assert.Equal(1, Journal.SubscriberCount));

        await DisposeComponentsAsync();

        await Eventually.HoldsAsync(() => Journal.SubscriberCount == 0);
    }

    [Fact]
    public void Chart_IsOnThePageAndFollowsNewQuotes()
    {
        RecordQuote(22.60m);
        var page = Render<DashboardPage>();
        Assert.Single(page.FindAll("[data-testid=chart-hit]"));

        RecordQuote(22.63m, secondsAfterNow: 30);

        page.WaitForAssertion(() => Assert.Equal(2, page.FindAll("[data-testid=chart-hit]").Count));
    }

    [Fact]
    public void History_IsOnThePageAndFollowsNewNotifications()
    {
        var page = Render<DashboardPage>();
        Assert.Contains("Nenhum alerta disparado até agora.", page.Markup, StringComparison.Ordinal);

        RecordNotification(TestData.SellAlert(22.70m));

        page.WaitForAssertion(() => Assert.Single(page.FindAll("table[aria-label='E-mails de alerta'] tbody tr")));
    }

    [Fact]
    public void History_OpenDrawerStaysOpenWhenNewQuotesArrive()
    {
        RecordNotification(TestData.SellAlert(22.70m));
        var page = Render<DashboardPage>();
        page.Find("table[aria-label='E-mails de alerta'] tbody tr").Click();

        RecordQuote(22.75m, secondsAfterNow: 30);

        page.WaitForAssertion(() => Assert.Equal("R$ 22,75", page.Find("[data-testid=last-price]").TextContent));
        Assert.Single(page.FindAll("[role=dialog]"));
    }

    [Fact]
    public void OneStock_HidesTheOverviewAndTheHistoryFilter()
    {
        var page = Render<DashboardPage>();

        Assert.Empty(page.FindAll("table[aria-label='Ativos monitorados']"));
        Assert.Empty(page.FindAll("select[aria-label='Filtrar por ativo']"));
    }

    [Fact]
    public void SeveralStocks_AreListedInTheOverviewWithTheFirstSelected()
    {
        Targets = TestData.PetrAndVale;
        RecordQuote(22.63m);
        RecordQuote(60.96m, TestData.Vale3);

        var page = Render<DashboardPage>();

        var rows = page.FindAll("table[aria-label='Ativos monitorados'] tbody tr");
        Assert.Equal(["PETR4", "VALE3"], rows.Select(row => row.QuerySelector("td")!.TextContent));
        Assert.Equal("true", rows[0].GetAttribute("aria-current"));
        Assert.Contains("Último preço · PETR4", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectingAStock_ShowsItsKpisChartAndEmailCount()
    {
        Targets = TestData.PetrAndVale;
        RecordQuote(22.63m);
        RecordQuote(60.00m, TestData.Vale3);
        RecordNotification(new PriceAlert(TestData.Vale3, AlertKind.Buy, 60.00m, 60.80m, TestData.Now));
        var page = Render<DashboardPage>();

        page.FindAll("table[aria-label='Ativos monitorados'] tbody tr")[1].Click();

        Assert.Equal("R$ 60,00", page.Find("[data-testid=last-price]").TextContent);
        Assert.Equal("Cotação de VALE3", page.Find("#chart-title").TextContent);
        Assert.Equal("1 enviado", page.Find("[data-testid=emails-sent]").TextContent);
        Assert.Equal("true", page.FindAll("table[aria-label='Ativos monitorados'] tbody tr")[1].GetAttribute("aria-current"));
    }

    [Fact]
    public void SelectedStock_StaysSelectedWhenNewQuotesArrive()
    {
        Targets = TestData.PetrAndVale;
        var page = Render<DashboardPage>();
        page.FindAll("table[aria-label='Ativos monitorados'] tbody tr")[1].KeyDown("Enter");

        RecordQuote(22.63m);
        RecordQuote(61.00m, TestData.Vale3);

        page.WaitForAssertion(() => Assert.Equal("R$ 61,00", page.Find("[data-testid=last-price]").TextContent));
    }

    [Fact]
    public void SwitchingStock_ClearsTheHoveredPoint()
    {
        Targets = TestData.PetrAndVale;
        RecordQuote(22.63m);
        RecordQuote(60.96m, TestData.Vale3);
        var page = Render<DashboardPage>();
        page.Find("[data-testid=chart-hit]").MouseOver();
        Assert.Single(page.FindAll("[role=tooltip]"));

        page.FindAll("table[aria-label='Ativos monitorados'] tbody tr")[1].Click();

        Assert.Empty(page.FindAll("[role=tooltip]"));
    }

    [Fact]
    public void History_CanBeFilteredByStock()
    {
        Targets = TestData.PetrAndVale;
        RecordNotification(TestData.SellAlert());
        RecordNotification(new PriceAlert(TestData.Vale3, AlertKind.Buy, 60.00m, 60.80m, TestData.Now), secondsAfterNow: 30);
        var page = Render<DashboardPage>();
        var filter = page.Find("select[aria-label='Filtrar por ativo']");
        Assert.Equal(["Todos os ativos", "PETR4", "VALE3"], filter.QuerySelectorAll("option").Select(option => option.TextContent));
        Assert.Equal(2, page.FindAll("table[aria-label='E-mails de alerta'] tbody tr").Count);

        filter.Change("VALE3");

        var row = Assert.Single(page.FindAll("table[aria-label='E-mails de alerta'] tbody tr"));
        Assert.Contains("VALE3", row.TextContent, StringComparison.Ordinal);
        Assert.Equal("1", page.Find("#notifications-title").ParentElement!.ParentElement!.QuerySelector(".count-pill")!.TextContent);

        page.Find("select[aria-label='Filtrar por ativo']").Change("");

        Assert.Equal(2, page.FindAll("table[aria-label='E-mails de alerta'] tbody tr").Count);
    }

    [Fact]
    public void History_FilteredStockWithoutAlerts_SaysSo()
    {
        Targets = TestData.PetrAndVale;
        RecordNotification(TestData.SellAlert());
        var page = Render<DashboardPage>();

        page.Find("select[aria-label='Filtrar por ativo']").Change("VALE3");

        Assert.Contains("Nenhum alerta de VALE3 disparado até agora.", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void TransientFailure_ShowsAWarningUntilANewerQuoteArrives()
    {
        RecordQuote(22.63m);
        RecordFailure(MonitoringFailureKind.Transient, secondsAfterNow: 30);
        var page = Render<DashboardPage>();
        Assert.Contains("Falha temporária ao verificar PETR4", page.Find("[role=alert]").TextContent, StringComparison.Ordinal);

        RecordQuote(22.64m, secondsAfterNow: 60);

        page.WaitForAssertion(() => Assert.Empty(page.FindAll("[role=alert]")));
        Assert.Contains("Ao vivo", page.Find("[role=status]").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void UnrecoverableFailure_ShowsACriticalBannerThatStays()
    {
        RecordQuote(22.63m);
        var page = Render<DashboardPage>();

        RecordFailure(MonitoringFailureKind.TickerNotFound, reason: "Ticker 'PETR4' was not found by the quote provider.", secondsAfterNow: 30);

        page.WaitForAssertion(() => Assert.Contains("Monitoramento de PETR4 interrompido", page.Find(".banner--critical").TextContent, StringComparison.Ordinal));
        Assert.DoesNotContain("Os demais ativos", page.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void OneStockStopped_OthersKeepRunningAndThePageSaysSo()
    {
        Targets = TestData.PetrAndVale;
        RecordQuote(22.63m);
        RecordFailure(MonitoringFailureKind.TickerNotFound, TestData.Vale3, "not found");

        var page = Render<DashboardPage>();

        Assert.Contains("Monitoramento de VALE3 interrompido", page.Find(".banner--critical").TextContent, StringComparison.Ordinal);
        Assert.Contains("Os demais ativos continuam sendo monitorados.", page.Markup, StringComparison.Ordinal);
        Assert.Contains("Instável", page.Find("[role=status]").TextContent, StringComparison.Ordinal);
        Assert.Contains("Interrompido", page.FindAll("table[aria-label='Ativos monitorados'] tbody tr")[1].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void SameFailureOnSeveralStocks_IsGroupedInOneBanner()
    {
        Targets = TestData.PetrAndVale;
        RecordFailure(MonitoringFailureKind.Transient, TestData.Petr4, "HTTP 503");
        RecordFailure(MonitoringFailureKind.Transient, TestData.Vale3, "HTTP 503");

        var page = Render<DashboardPage>();

        var banner = Assert.Single(page.FindAll("[role=alert]"));
        Assert.Contains("Falha temporária ao verificar 2 ativos", banner.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void StoppedAndUnstableStocks_GetSeparateBanners()
    {
        Targets = TestData.PetrAndVale;
        RecordFailure(MonitoringFailureKind.Transient, TestData.Petr4, "HTTP 503");
        RecordFailure(MonitoringFailureKind.TickerNotFound, TestData.Vale3, "not found");

        var page = Render<DashboardPage>();

        Assert.Single(page.FindAll(".banner--critical"));
        Assert.Single(page.FindAll(".banner--warning"));
    }

    [Fact]
    public void KpisDescribeTheFirstStockOfSeveral()
    {
        Targets = new MonitoringTargets([TestData.ValeTarget, TestData.Target]);

        Assert.Contains("Último preço · VALE3", Render<DashboardPage>().Markup, StringComparison.Ordinal);
    }
}