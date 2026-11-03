using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockQuoteAlert.Cli;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Cli;

public sealed class ConfigurationErrorsTests
{
    private const string WrappedRecipient =
        "DataAnnotation validation failed for 'EmailOptions' members: 'Recipient' with the error: 'Email:Recipient is required.'.";

    [Fact]
    public void TryExtract_ValidationFailure_ReturnsTheMessageWithoutTheWrapper()
    {
        var exception = new OptionsValidationException("", typeof(object), [WrappedRecipient]);

        Assert.True(ConfigurationErrors.TryExtract(exception, out var failures));
        Assert.Equal(["Email:Recipient is required."], failures);
    }

    [Fact]
    public void TryExtract_MessageWithQuotes_IsUnwrappedWhole()
    {
        var wrapped = "DataAnnotation validation failed for 'SmtpOptions' members: 'Security' with the error: 'Smtp:Security None isn't allowed'.";
        var exception = new OptionsValidationException("", typeof(object), [wrapped]);

        Assert.True(ConfigurationErrors.TryExtract(exception, out var failures));
        Assert.Equal(["Smtp:Security None isn't allowed"], failures);
    }

    [Fact]
    public void TryExtract_FailureNotFromDataAnnotations_IsKeptAsIs()
    {
        var exception = new OptionsValidationException("", typeof(object), ["Custom validator said no."]);

        Assert.True(ConfigurationErrors.TryExtract(exception, out var failures));
        Assert.Equal(["Custom validator said no."], failures);
    }

    [Fact]
    public void TryExtract_SeveralSections_ReturnsEveryFailureInOrder()
    {
        var exception = new AggregateException(
            new OptionsValidationException("", typeof(object), [WrappedRecipient, "DataAnnotation validation failed for 'EmailOptions' members: 'SenderAddress' with the error: 'Email:SenderAddress is required.'."]),
            new OptionsValidationException("", typeof(object), ["DataAnnotation validation failed for 'SmtpOptions' members: 'Host' with the error: 'Smtp:Host is required.'."]));

        Assert.True(ConfigurationErrors.TryExtract(exception, out var failures));
        Assert.Equal(["Email:Recipient is required.", "Email:SenderAddress is required.", "Smtp:Host is required."], failures);
    }

    [Fact]
    public void TryExtract_NestedAggregates_AreFlattened()
    {
        var exception = new AggregateException(new AggregateException(new OptionsValidationException("", typeof(object), [WrappedRecipient])));

        Assert.True(ConfigurationErrors.TryExtract(exception, out var failures));
        Assert.Equal(["Email:Recipient is required."], failures);
    }

    [Fact]
    public void TryExtract_ValueThatCannotBeConverted_ReturnsTheBinderMessage()
    {
        var exception = new InvalidOperationException("Failed to convert configuration value 'abc' at 'Smtp:Port' to type 'System.Int32'.");

        Assert.True(ConfigurationErrors.TryExtract(exception, out var failures));
        Assert.Equal(["Failed to convert configuration value 'abc' at 'Smtp:Port' to type 'System.Int32'."], failures);
    }

    public static TheoryData<Exception> NotConfigurationErrors => new()
    {
        new InvalidOperationException("Something else went wrong."),
        new HttpRequestException("network down"),
        new AggregateException(new OptionsValidationException("", typeof(object), [WrappedRecipient]), new IOException("disk full")),
        new AggregateException(),
    };

    [Theory]
    [MemberData(nameof(NotConfigurationErrors))]
    public void TryExtract_OtherFailures_AreNotConfigurationErrors(Exception exception)
    {
        Assert.False(ConfigurationErrors.TryExtract(exception, out var failures));
        Assert.Empty(failures);
    }

    [Fact]
    public void TryExtract_WithNullException_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ConfigurationErrors.TryExtract(null!, out _));
    }

    [Theory]
    [InlineData("Email:Recipient", "", "Email:Recipient is required.")]
    [InlineData("Smtp:Port", "abc", "Failed to convert configuration value 'abc' at 'Smtp:Port' to type 'System.Int32'.")]
    [InlineData("Smtp:Security", "Bogus", "Failed to convert configuration value 'Bogus' at 'Smtp:Security'")]
    [InlineData("Monitoring:PollingInterval", "00:00:01", "Monitoring:PollingInterval must be between 5 seconds and 1 day.")]
    public async Task TryExtract_RealStartupFailure_IsRecognized(string key, string value, string expected)
    {
        var settings = AppSettings.Valid;
        settings[key] = value;
        using var host = StockQuoteAlertApp.Create(TestData.Targets, builder =>
        {
            builder.Configuration.AddInMemoryCollection(settings);
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
        });

        var exception = await Assert.ThrowsAnyAsync<Exception>(() => host.StartAsync(TestContext.Current.CancellationToken));

        Assert.True(ConfigurationErrors.TryExtract(exception, out var failures));
        Assert.StartsWith(expected, Assert.Single(failures), StringComparison.Ordinal);
    }
}