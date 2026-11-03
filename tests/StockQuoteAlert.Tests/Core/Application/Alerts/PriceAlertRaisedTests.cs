using StockQuoteAlert.Core.Application.Alerts;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Core.Application.Alerts;

public sealed class PriceAlertRaisedTests
{
    [Fact]
    public void ExposesIdAlertAndTime()
    {
        var id = Guid.CreateVersion7();

        var raised = new PriceAlertRaised(id, TestData.SellAlert(), TestData.Now);

        Assert.Equal(id, raised.Id);
        Assert.Equal(TestData.SellAlert(), raised.Alert);
        Assert.Equal(TestData.Now, raised.RaisedAt);
    }

    [Fact]
    public void ComparesByValue()
    {
        var id = Guid.CreateVersion7();

        Assert.Equal(new PriceAlertRaised(id, TestData.SellAlert(), TestData.Now), new PriceAlertRaised(id, TestData.SellAlert(), TestData.Now));
        Assert.NotEqual(new PriceAlertRaised(id, TestData.SellAlert(), TestData.Now), new PriceAlertRaised(Guid.CreateVersion7(), TestData.SellAlert(), TestData.Now));
    }
}