using Bunit;
using StockQuoteAlert.Cli.Components.Dashboard;
using StockQuoteAlert.Core.Application.Journal;
using StockQuoteAlert.Core.Domain;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Cli.Components;

public sealed class FailureBannerTests : DashboardTestContext
{
    [Fact]
    public void TransientFailure_IsAWarningThatExplainsTheRetry()
    {
        var banner = RenderBanner([Failure(TestData.Petr4, MonitoringFailureKind.Transient, "Yahoo Finance responded with HTTP 503 for PETR4.")]);

        var alert = banner.Find("[role=alert]");
        Assert.Contains("banner--warning", alert.ClassName, StringComparison.Ordinal);
        Assert.Equal("!", alert.QuerySelector(".banner__icon")!.TextContent);
        Assert.Equal("Falha temporária ao verificar PETR4", alert.QuerySelector("strong")!.TextContent);
        Assert.Contains("Não foi possível obter a cotação de PETR4 agora. Uma nova tentativa é feita a cada verificação.", alert.TextContent, StringComparison.Ordinal);
        Assert.Contains("· 29/10/2026 14:30:00", alert.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnicalReason_IsKeptApartUnderDetails()
    {
        var banner = RenderBanner([Failure(TestData.Petr4, MonitoringFailureKind.Transient, "Yahoo Finance responded with HTTP 503 for PETR4.")]);

        Assert.Equal("Detalhes técnicos", banner.Find("details summary").TextContent);
        Assert.Contains("PETR4: Yahoo Finance responded with HTTP 503 for PETR4.", banner.Find(".banner__list li").TextContent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(MonitoringFailureKind.TickerNotFound, "não foi encontrado pelos provedores de cotação")]
    [InlineData(MonitoringFailureKind.Rejected, "O provedor de cotações recusou a consulta de PETR4")]
    public void UnrecoverableFailure_IsCritical(MonitoringFailureKind kind, string explanation)
    {
        var alert = RenderBanner([Failure(TestData.Petr4, kind, "reason")]).Find("[role=alert]");

        Assert.Contains("banner--critical", alert.ClassName, StringComparison.Ordinal);
        Assert.Equal("✕", alert.QuerySelector(".banner__icon")!.TextContent);
        Assert.Equal("Monitoramento de PETR4 interrompido", alert.QuerySelector("strong")!.TextContent);
        Assert.Contains(explanation, alert.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void SameFailureOnSeveralStocks_IsOneParagraphNamingThemAll()
    {
        var banner = RenderBanner([
            Failure(TestData.Petr4, MonitoringFailureKind.Transient, "HTTP 503"),
            Failure(TestData.Vale3, MonitoringFailureKind.Transient, "HTTP 503"),
        ]);

        Assert.Equal("Falha temporária ao verificar 2 ativos", banner.Find("strong").TextContent);
        var paragraph = Assert.Single(banner.FindAll(".banner > div > p"));
        Assert.Contains("PETR4 e VALE3", paragraph.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("29/10/2026", paragraph.TextContent, StringComparison.Ordinal);
        Assert.Equal(2, banner.FindAll(".banner__list li").Count);
    }

    [Fact]
    public void DifferentUnrecoverableKinds_GetOneParagraphEach()
    {
        var banner = RenderBanner([
            Failure(TestData.Petr4, MonitoringFailureKind.TickerNotFound, "not found"),
            Failure(TestData.Vale3, MonitoringFailureKind.Rejected, "HTTP 401"),
        ]);

        Assert.Equal(2, banner.FindAll(".banner > div > p").Count);
    }

    [Fact]
    public void StoppedStockWhileOthersRun_SaysTheOthersContinue()
    {
        var banner = RenderBanner([Failure(TestData.Vale3, MonitoringFailureKind.TickerNotFound, "not found")], othersStillMonitored: true);

        Assert.Contains("Os demais ativos continuam sendo monitorados.", banner.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void TransientFailure_NeverSaysTheOthersContinue()
    {
        var banner = RenderBanner([Failure(TestData.Vale3, MonitoringFailureKind.Transient, "HTTP 503")], othersStillMonitored: true);

        Assert.DoesNotContain("Os demais ativos", banner.Markup, StringComparison.Ordinal);
    }

    private IRenderedComponent<FailureBanner> RenderBanner(IReadOnlyList<MonitoringFailure> failures, bool othersStillMonitored = false) =>
        Render<FailureBanner>(parameters => parameters
            .Add(p => p.Failures, failures)
            .Add(p => p.OthersStillMonitored, othersStillMonitored));

    private static MonitoringFailure Failure(Ticker ticker, MonitoringFailureKind kind, string reason) =>
        new(ticker, TestData.Now, reason, kind);
}