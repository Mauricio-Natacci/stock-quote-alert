using StockQuoteAlert.Cli.Dashboard;
using StockQuoteAlert.Core.Application.Journal;
using StockQuoteAlert.Core.Domain;
using StockQuoteAlert.Infrastructure.Quotes;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Cli.Dashboard;

public sealed class DashboardContractsTests
{
    [Fact]
    public void TargetResponse_From_CopiesTickerAndReferences()
    {
        Assert.Equal(new TargetResponse("VALE3", 61.50m, 60.80m), TargetResponse.From(TestData.ValeTarget));
    }

    [Fact]
    public void QuoteResponse_From_FlattensTheObservation()
    {
        var observation = new QuoteObservation(new Quote(TestData.Petr4, 22.70m, TestData.Now, "brapi.dev"), PriceZone.AboveSellThreshold, TestData.Now.AddSeconds(1));

        Assert.Equal(
            new QuoteResponse("PETR4", 22.70m, PriceZone.AboveSellThreshold, "brapi.dev", TestData.Now, TestData.Now.AddSeconds(1)),
            QuoteResponse.From(observation));
    }

    [Fact]
    public void NotificationResponse_From_FlattensTheAlert()
    {
        var id = Guid.CreateVersion7();
        var record = new NotificationRecord(id, TestData.BuyAlert(22.50m), "E-mail", "dest@gmail.com", "subject", "body", TestData.Now.AddSeconds(2), NotificationStatus.Failed, "boom");

        Assert.Equal(
            new NotificationResponse(id, "PETR4", AlertKind.Buy, 22.50m, 22.59m, TestData.Now, "E-mail", "dest@gmail.com", "subject", "body", TestData.Now.AddSeconds(2), NotificationStatus.Failed, "boom"),
            NotificationResponse.From(record));
    }

    [Fact]
    public void FailureResponse_From_IncludesWhetherItIsRecoverable()
    {
        var failure = new MonitoringFailure(TestData.Vale3, TestData.Now, "not found", MonitoringFailureKind.TickerNotFound);

        Assert.Equal(new FailureResponse("VALE3", TestData.Now, "not found", MonitoringFailureKind.TickerNotFound, false), FailureResponse.From(failure));
    }

    [Fact]
    public void MonitoringSession_KeepsTheStartTime()
    {
        Assert.Equal(TestData.Now, new MonitoringSession(TestData.Now).StartedAt);
    }

    [Theory]
    [InlineData(QuoteProviderKind.Yahoo, "Yahoo Finance")]
    [InlineData(QuoteProviderKind.Brapi, "brapi.dev")]
    [InlineData((QuoteProviderKind)9, "9")]
    public void ProviderDisplayName_MatchesTheNameQuotesAreTaggedWith(QuoteProviderKind kind, string expected)
    {
        Assert.Equal(expected, kind.DisplayName());
    }
}