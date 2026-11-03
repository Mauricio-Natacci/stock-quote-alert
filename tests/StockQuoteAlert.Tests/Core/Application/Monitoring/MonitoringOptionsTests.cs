using StockQuoteAlert.Core.Application.Monitoring;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Core.Application.Monitoring;

public sealed class MonitoringOptionsTests
{
    [Fact]
    public void SectionName_IsMonitoring()
    {
        Assert.Equal("Monitoring", MonitoringOptions.SectionName);
    }

    [Fact]
    public void PollingInterval_DefaultsTo30Seconds()
    {
        var options = new MonitoringOptions();

        Assert.Equal(TimeSpan.FromSeconds(30), options.PollingInterval);
        Assert.Empty(OptionsValidation.Errors(options));
    }

    [Theory]
    [InlineData("00:00:05")]
    [InlineData("00:01:00")]
    [InlineData("1.00:00:00")]
    public void Validate_WithIntervalInRange_Passes(string interval)
    {
        var options = new MonitoringOptions { PollingInterval = Parse(interval) };

        Assert.Empty(OptionsValidation.Errors(options));
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("00:00:04.999")]
    [InlineData("1.00:00:00.001")]
    [InlineData("-00:00:30")]
    public void Validate_WithIntervalOutOfRange_Fails(string interval)
    {
        var options = new MonitoringOptions { PollingInterval = Parse(interval) };

        Assert.Equal("Monitoring:PollingInterval must be between 5 seconds and 1 day.", Assert.Single(OptionsValidation.Errors(options)));
    }

    private static TimeSpan Parse(string value) => TimeSpan.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
}