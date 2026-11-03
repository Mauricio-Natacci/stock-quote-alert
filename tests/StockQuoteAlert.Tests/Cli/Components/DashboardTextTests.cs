using StockQuoteAlert.Cli.Components.Dashboard;
using StockQuoteAlert.Core.Application.Journal;
using StockQuoteAlert.Core.Application.Monitoring;
using StockQuoteAlert.Core.Domain;
using StockQuoteAlert.Infrastructure.Quotes;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Cli.Components;

public sealed class DashboardTextTests
{
    [Fact]
    public void Provider_NamesTheProviderAndTheFallback()
    {
        Assert.Equal("Yahoo Finance", DashboardText.Provider(new QuoteProviderOptions()));
        Assert.Equal("brapi.dev (reserva: Yahoo Finance)", DashboardText.Provider(new QuoteProviderOptions { Provider = QuoteProviderKind.Brapi, Fallback = QuoteProviderKind.Yahoo }));
    }

    [Theory]
    [InlineData(PriceZone.AboveSellThreshold, "Acima da linha de venda", "▲")]
    [InlineData(PriceZone.BelowBuyThreshold, "Abaixo da linha de compra", "▼")]
    [InlineData(PriceZone.Neutral, "Na zona neutra", "●")]
    public void Zone_HasALabelAndAnIcon(PriceZone zone, string label, string icon)
    {
        Assert.Equal(label, DashboardText.Zone(zone));
        Assert.Equal(icon, DashboardText.ZoneIcon(zone));
    }

    [Theory]
    [InlineData(22.70, "R$ 0,03 (0,13%) acima da linha de venda")]
    [InlineData(22.50, "R$ 0,09 (0,40%) abaixo da linha de compra")]
    [InlineData(22.65, "faltam R$ 0,02 (0,09%) para a linha de venda")]
    [InlineData(22.61, "faltam R$ 0,02 (0,09%) para a linha de compra")]
    [InlineData(22.63, "faltam R$ 0,04 (0,18%) para a linha de venda")]
    [InlineData(22.67, "faltam R$ 0,00 (0,00%) para a linha de venda")]
    public void Distance_MeasuresFromTheLineThatMatters(decimal price, string expected)
    {
        Assert.Equal(expected, DashboardText.Distance(price, TestData.Thresholds));
    }

    [Fact]
    public void Deviation_IsSignedAgainstTheCrossedLine()
    {
        Assert.Equal("+0,13%", DashboardText.Deviation(TestData.SellAlert(22.70m)));
        Assert.Equal("−0,40%", DashboardText.Deviation(TestData.BuyAlert(22.50m)));
    }

    [Theory]
    [InlineData(0, "enviados")]
    [InlineData(1, "enviado")]
    [InlineData(2, "enviados")]
    public void Plural_IsSingularOnlyForOne(int count, string expected)
    {
        Assert.Equal(expected, DashboardText.Plural(count, "enviado", "enviados"));
    }

    [Theory]
    [InlineData(null, MonitoringHealth.Live)]
    [InlineData(MonitoringFailureKind.Transient, MonitoringHealth.Unstable)]
    [InlineData(MonitoringFailureKind.TickerNotFound, MonitoringHealth.Stopped)]
    [InlineData(MonitoringFailureKind.Rejected, MonitoringHealth.Stopped)]
    public void Health_FollowsTheActiveFailure(MonitoringFailureKind? kind, MonitoringHealth expected)
    {
        var failure = kind is { } value ? new MonitoringFailure(TestData.Petr4, TestData.Now, "reason", value) : null;

        Assert.Equal(expected, DashboardText.Health(failure));
    }

    [Theory]
    [InlineData(new[] { MonitoringHealth.Live, MonitoringHealth.Live }, MonitoringHealth.Live)]
    [InlineData(new[] { MonitoringHealth.Live, MonitoringHealth.Unstable }, MonitoringHealth.Unstable)]
    [InlineData(new[] { MonitoringHealth.Live, MonitoringHealth.Stopped }, MonitoringHealth.Unstable)]
    [InlineData(new[] { MonitoringHealth.Stopped, MonitoringHealth.Stopped }, MonitoringHealth.Stopped)]
    [InlineData(new[] { MonitoringHealth.Stopped }, MonitoringHealth.Stopped)]
    public void OverallHealth_IsStoppedOnlyWhenEveryStockIs(MonitoringHealth[] healths, MonitoringHealth expected)
    {
        var tickers = healths.Select((health, index) => TickerWith(index, health)).ToList();

        Assert.Equal(expected, DashboardText.OverallHealth(tickers));
    }

    [Theory]
    [InlineData(MonitoringHealth.Live, "Ao vivo", "live")]
    [InlineData(MonitoringHealth.Unstable, "Instável", "warning")]
    [InlineData(MonitoringHealth.Stopped, "Interrompido", "stopped")]
    public void Connection_HasALabelAndACssModifier(MonitoringHealth health, string label, string modifier)
    {
        Assert.Equal(label, DashboardText.ConnectionState(health));
        Assert.Equal(modifier, DashboardText.ConnectionModifier(health));
    }

    [Fact]
    public void FailureSummary_ExplainsEachKindForOneOrSeveralStocks()
    {
        Assert.Equal(
            "Não foi possível obter a cotação de PETR4 agora. Uma nova tentativa é feita a cada verificação.",
            DashboardText.FailureSummary(MonitoringFailureKind.Transient, ["PETR4"]));
        Assert.Equal(
            "PETR4 não foi encontrado pelos provedores de cotação. Confira se o código está correto e reinicie o monitoramento.",
            DashboardText.FailureSummary(MonitoringFailureKind.TickerNotFound, ["PETR4"]));
        Assert.Equal(
            "PETR4 e VALE3 não foram encontrados pelos provedores de cotação. Confira se os códigos estão corretos e reinicie o monitoramento.",
            DashboardText.FailureSummary(MonitoringFailureKind.TickerNotFound, ["PETR4", "VALE3"]));
        Assert.Equal(
            "O provedor de cotações recusou a consulta de PETR4. Confira a configuração do provedor nos detalhes técnicos.",
            DashboardText.FailureSummary(MonitoringFailureKind.Rejected, ["PETR4"]));
    }

    [Theory]
    [InlineData(new string[0], "")]
    [InlineData(new[] { "A" }, "A")]
    [InlineData(new[] { "A", "B" }, "A e B")]
    [InlineData(new[] { "A", "B", "C" }, "A, B e C")]
    public void Names_AreJoinedThePortugueseWay(string[] names, string expected)
    {
        Assert.Equal(expected, DashboardText.Names(names));
    }

    [Theory]
    [InlineData(1, "1 ativo")]
    [InlineData(3, "3 ativos")]
    public void StockCount_CountsStocks(int count, string expected)
    {
        Assert.Equal(expected, DashboardText.StockCount(count));
    }

    [Fact]
    public void ActionAndStatus_HaveLabelsAndIcons()
    {
        Assert.Equal(("Venda", "▲"), (DashboardText.Action(AlertKind.Sell), DashboardText.ActionIcon(AlertKind.Sell)));
        Assert.Equal(("Compra", "▼"), (DashboardText.Action(AlertKind.Buy), DashboardText.ActionIcon(AlertKind.Buy)));
        Assert.Equal(("Enviado", "✓"), (DashboardText.Status(NotificationStatus.Sent), DashboardText.StatusIcon(NotificationStatus.Sent)));
        Assert.Equal(("Falhou", "✕"), (DashboardText.Status(NotificationStatus.Failed), DashboardText.StatusIcon(NotificationStatus.Failed)));
    }

    [Theory]
    [InlineData(30, "30 s")]
    [InlineData(5, "5 s")]
    [InlineData(60, "1 min")]
    [InlineData(300, "5 min")]
    [InlineData(90, "90 s")]
    public void Interval_IsShortAndReadable(int seconds, string expected)
    {
        Assert.Equal(expected, DashboardText.Interval(TimeSpan.FromSeconds(seconds)));
    }

    [Theory]
    [InlineData(0, "há 0 s")]
    [InlineData(59, "há 59 s")]
    [InlineData(60, "há 1 min")]
    [InlineData(3599, "há 59 min")]
    [InlineData(7200, "há 2 h")]
    [InlineData(-5, "há 0 s")]
    public void Relative_DescribesHowLongAgo(int secondsAgo, string expected)
    {
        Assert.Equal(expected, DashboardText.Relative(TestData.Now, TestData.Now.AddSeconds(secondsAgo)));
    }

    private static TickerDashboard TickerWith(int index, MonitoringHealth health)
    {
        var target = new MonitoringTarget(Ticker.Create($"ABCD{index + 3}"), TestData.Thresholds);
        var failure = health switch
        {
            MonitoringHealth.Unstable => new MonitoringFailure(target.Ticker, TestData.Now, "reason", MonitoringFailureKind.Transient),
            MonitoringHealth.Stopped => new MonitoringFailure(target.Ticker, TestData.Now, "reason", MonitoringFailureKind.TickerNotFound),
            _ => null,
        };

        return new TickerDashboard(target, [], [], failure);
    }
}