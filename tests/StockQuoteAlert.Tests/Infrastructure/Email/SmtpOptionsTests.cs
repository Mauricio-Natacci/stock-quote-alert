using StockQuoteAlert.Infrastructure.Email;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Infrastructure.Email;

public sealed class SmtpOptionsTests
{
    [Fact]
    public void SectionName_IsSmtp()
    {
        Assert.Equal("Smtp", SmtpOptions.SectionName);
    }

    [Fact]
    public void Defaults_UseAuthenticatedStartTlsOnPort587()
    {
        var options = new SmtpOptions();

        Assert.Equal(string.Empty, options.Host);
        Assert.Equal(587, options.Port);
        Assert.Equal(SmtpSecurity.StartTls, options.Security);
        Assert.True(options.RequireAuthentication);
        Assert.False(options.CheckCertificateRevocation);
        Assert.Equal(string.Empty, options.Username);
        Assert.Equal(string.Empty, options.Password);
        Assert.Equal(TimeSpan.FromSeconds(30), options.Timeout);
    }

    [Fact]
    public void Validate_WithValidOptions_Passes()
    {
        Assert.Empty(OptionsValidation.Errors(TestData.SmtpOptions));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_WithoutHost_Fails(string? host)
    {
        var options = TestData.SmtpOptions;
        options.Host = host!;

        Assert.Equal("Smtp:Host is required.", Assert.Single(OptionsValidation.Errors(options)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(25)]
    [InlineData(465)]
    [InlineData(65535)]
    public void Validate_WithPortInRange_Passes(int port)
    {
        var options = TestData.SmtpOptions;
        options.Port = port;

        Assert.Empty(OptionsValidation.Errors(options));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void Validate_WithPortOutOfRange_Fails(int port)
    {
        var options = TestData.SmtpOptions;
        options.Port = port;

        Assert.Equal("Smtp:Port must be between 1 and 65535.", Assert.Single(OptionsValidation.Errors(options)));
    }

    [Theory]
    [InlineData(SmtpSecurity.StartTls)]
    [InlineData(SmtpSecurity.SslOnConnect)]
    public void Validate_WithEncryptedSecurity_Passes(SmtpSecurity security)
    {
        var options = TestData.SmtpOptions;
        options.Security = security;

        Assert.Empty(OptionsValidation.Errors(options));
    }

    [Fact]
    public void Validate_WithUndefinedSecurity_Fails()
    {
        var options = TestData.SmtpOptions;
        options.Security = (SmtpSecurity)42;

        Assert.Equal("Smtp:Security must be StartTls, SslOnConnect or None.", Assert.Single(OptionsValidation.Errors(options)));
    }

    [Theory]
    [InlineData("00:00:01")]
    [InlineData("00:00:30")]
    [InlineData("00:05:00")]
    public void Validate_WithTimeoutInRange_Passes(string timeout)
    {
        var options = TestData.SmtpOptions;
        options.Timeout = TimeSpan.Parse(timeout, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Empty(OptionsValidation.Errors(options));
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("00:00:00.999")]
    [InlineData("00:05:00.001")]
    [InlineData("-00:00:10")]
    public void Validate_WithTimeoutOutOfRange_Fails(string timeout)
    {
        var options = TestData.SmtpOptions;
        options.Timeout = TimeSpan.Parse(timeout, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal("Smtp:Timeout must be between 1 second and 5 minutes.", Assert.Single(OptionsValidation.Errors(options)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_WithAuthenticationButNoUsername_Fails(string? username)
    {
        var options = TestData.SmtpOptions;
        options.Username = username!;

        Assert.Equal(
            "Smtp:Username is required when Smtp:RequireAuthentication is true.",
            Assert.Single(OptionsValidation.Errors(options)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_WithAuthenticationButNoPassword_Fails(string? password)
    {
        var options = TestData.SmtpOptions;
        options.Password = password!;

        Assert.Equal(
            "Smtp:Password is required when Smtp:RequireAuthentication is true (set it through user-secrets or the Smtp__Password environment variable).",
            Assert.Single(OptionsValidation.Errors(options)));
    }

    [Fact]
    public void Validate_WithAuthenticationButNoCredentials_ReportsBoth()
    {
        var options = TestData.SmtpOptions;
        options.Username = "";
        options.Password = "";

        var errors = OptionsValidation.Errors(options);

        Assert.Equal(2, errors.Count);
        Assert.StartsWith("Smtp:Username is required", errors[0], StringComparison.Ordinal);
        Assert.StartsWith("Smtp:Password is required", errors[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_WithAuthenticationOverUnencryptedConnection_Fails()
    {
        var options = TestData.SmtpOptions;
        options.Security = SmtpSecurity.None;

        Assert.Equal(
            "Smtp:Security None would send the password unencrypted; use StartTls or SslOnConnect, or set Smtp:RequireAuthentication to false.",
            Assert.Single(OptionsValidation.Errors(options)));
    }

    [Theory]
    [InlineData(SmtpSecurity.None)]
    [InlineData(SmtpSecurity.StartTls)]
    public void Validate_WithoutAuthentication_CredentialsAreOptional(SmtpSecurity security)
    {
        var options = new SmtpOptions
        {
            Host = "localhost",
            Port = 1025,
            Security = security,
            RequireAuthentication = false,
        };

        Assert.Empty(OptionsValidation.Errors(options));
    }

    [Fact]
    public void Validate_WhenAttributesFail_SkipsCrossFieldRules()
    {
        // DataAnnotations runs IValidatableObject only after every attribute passes,
        // so a missing host is reported first and the credentials are checked once it is fixed.
        var options = new SmtpOptions();

        Assert.Equal("Smtp:Host is required.", Assert.Single(OptionsValidation.Errors(options)));
    }
}