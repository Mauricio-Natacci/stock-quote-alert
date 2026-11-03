using StockQuoteAlert.Infrastructure.Email;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Infrastructure.Email;

public sealed class EmailOptionsTests
{
    [Fact]
    public void SectionName_IsEmail()
    {
        Assert.Equal("Email", EmailOptions.SectionName);
    }

    [Fact]
    public void Defaults_HaveSenderNameButNoAddresses()
    {
        var options = new EmailOptions();

        Assert.Equal("Stock Quote Alert", options.SenderName);
        Assert.Equal(
            ["Email:Recipient is required.", "Email:SenderAddress is required."],
            OptionsValidation.Errors(options));
    }

    [Fact]
    public void Validate_WithValidOptions_Passes()
    {
        Assert.Empty(OptionsValidation.Errors(TestData.EmailOptions));
    }

    [Theory]
    [InlineData("me@gmail.com")]
    [InlineData("first.last+alerts@sub.example.com.br")]
    public void Validate_WithValidAddress_Passes(string address)
    {
        var options = TestData.EmailOptions;
        options.Recipient = address;
        options.SenderAddress = address;

        Assert.Empty(OptionsValidation.Errors(options));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_WithoutRecipient_Fails(string? recipient)
    {
        var options = TestData.EmailOptions;
        options.Recipient = recipient!;

        Assert.Equal("Email:Recipient is required.", Assert.Single(OptionsValidation.Errors(options)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_WithoutSenderAddress_Fails(string? senderAddress)
    {
        var options = TestData.EmailOptions;
        options.SenderAddress = senderAddress!;

        Assert.Equal("Email:SenderAddress is required.", Assert.Single(OptionsValidation.Errors(options)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_WithoutSenderName_Fails(string senderName)
    {
        var options = TestData.EmailOptions;
        options.SenderName = senderName;

        Assert.Equal("Email:SenderName is required.", Assert.Single(OptionsValidation.Errors(options)));
    }

    public static TheoryData<string> InvalidAddresses => new()
    {
        "not-an-email",
        "@gmail.com",
        "me@",
        "Me <me@gmail.com>",
        "me@gmail.com, other@gmail.com",
        "me@gmail.com\r\nBcc: attacker@example.com",
        " me@gmail.com",
    };

    [Theory]
    [MemberData(nameof(InvalidAddresses))]
    public void Validate_WithInvalidRecipient_Fails(string recipient)
    {
        var options = TestData.EmailOptions;
        options.Recipient = recipient;

        Assert.Equal("Email:Recipient must be a valid e-mail address.", Assert.Single(OptionsValidation.Errors(options)));
    }

    [Theory]
    [MemberData(nameof(InvalidAddresses))]
    public void Validate_WithInvalidSenderAddress_Fails(string senderAddress)
    {
        var options = TestData.EmailOptions;
        options.SenderAddress = senderAddress;

        Assert.Equal("Email:SenderAddress must be a valid e-mail address.", Assert.Single(OptionsValidation.Errors(options)));
    }

    [Fact]
    public void Validate_WithBothAddressesInvalid_ReportsBoth()
    {
        var options = TestData.EmailOptions;
        options.Recipient = "x";
        options.SenderAddress = "y";

        Assert.Equal(
            ["Email:Recipient must be a valid e-mail address.", "Email:SenderAddress must be a valid e-mail address."],
            OptionsValidation.Errors(options));
    }
}