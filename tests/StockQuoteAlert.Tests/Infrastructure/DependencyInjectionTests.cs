using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using StockQuoteAlert.Core.Application.Abstractions;
using StockQuoteAlert.Core.Application.Exceptions;
using StockQuoteAlert.Core.Domain;
using StockQuoteAlert.Infrastructure;
using StockQuoteAlert.Infrastructure.Email;
using StockQuoteAlert.Infrastructure.Quotes;
using StockQuoteAlert.Infrastructure.Quotes.Yahoo;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Infrastructure;

public sealed class DependencyInjectionTests : IDisposable
{
    /// <summary>Retries in the standard pipeline: 3 retries after the first attempt.</summary>
    private const int StandardAttempts = 4;

    /// <summary>The minimum valid configuration; each test overrides what it needs.</summary>
    private static readonly Dictionary<string, string?> ValidSettings = new()
    {
        ["Email:Recipient"] = "dest@gmail.com",
        ["Email:SenderAddress"] = "me@gmail.com",
        ["Smtp:Host"] = "smtp.gmail.com",
        ["Smtp:Username"] = "me@gmail.com",
        ["Smtp:Password"] = "app-password",
    };

    private readonly FakeQuoteApi _api = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void AddInfrastructure_RegistersQuoteProviderAsSingletonAdapter()
    {
        using var services = BuildProvider();

        var provider = services.GetRequiredService<IQuoteProvider>();

        Assert.IsType<QuoteSourceAdapter>(provider);
        Assert.Same(provider, services.GetRequiredService<IQuoteProvider>());
    }

    [Fact]
    public void AddInfrastructure_RegistersEmailComposerAsSingleton()
    {
        using var services = BuildProvider();

        var composer = services.GetRequiredService<IAlertEmailComposer>();

        Assert.IsType<AlertEmailComposer>(composer);
        Assert.Same(composer, services.GetRequiredService<IAlertEmailComposer>());
    }

    [Fact]
    public void AddInfrastructure_RegistersMailKitSenderAsSingleton()
    {
        using var services = BuildProvider();

        var sender = services.GetRequiredService<IEmailSender>();

        Assert.IsType<MailKitEmailSender>(sender);
        Assert.Same(sender, services.GetRequiredService<IEmailSender>());
        Assert.IsType<SmtpClientFactory>(services.GetRequiredService<ISmtpClientFactory>());
    }

    [Fact]
    public void AddInfrastructure_RegistersEmailNotifierAsSingleton()
    {
        using var services = BuildProvider();

        var notifier = services.GetRequiredService<IAlertNotifier>();

        Assert.IsType<EmailAlertNotifier>(notifier);
        Assert.Same(notifier, services.GetRequiredService<IAlertNotifier>());
    }

    [Fact]
    public void AddInfrastructure_WithNullServices_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => DependencyInjection.AddInfrastructure(null!));
    }

    [Fact]
    public void YahooOptions_WithoutConfiguration_UseDefaults()
    {
        using var services = BuildProvider();

        var options = services.GetRequiredService<IOptions<YahooFinanceOptions>>().Value;

        Assert.Equal(new Uri("https://query1.finance.yahoo.com/"), options.BaseUrl);
        Assert.Equal(new YahooFinanceOptions().UserAgent, options.UserAgent);
    }

    [Fact]
    public void YahooOptions_AreBoundFromConfiguration()
    {
        using var services = BuildProvider(new()
        {
            ["QuoteProvider:Yahoo:BaseUrl"] = "https://yahoo.test/",
            ["QuoteProvider:Yahoo:UserAgent"] = "custom-agent/2.0",
        });

        var options = services.GetRequiredService<IOptions<YahooFinanceOptions>>().Value;

        Assert.Equal(new Uri("https://yahoo.test/"), options.BaseUrl);
        Assert.Equal("custom-agent/2.0", options.UserAgent);
    }

    [Theory]
    [InlineData("QuoteProvider:Yahoo:BaseUrl", "https://yahoo.test/api", "QuoteProvider:Yahoo:BaseUrl must end with '/'.")]
    [InlineData("QuoteProvider:Yahoo:BaseUrl", "finance/", "QuoteProvider:Yahoo:BaseUrl must be an absolute http or https URL.")]
    [InlineData("QuoteProvider:Yahoo:UserAgent", "", "QuoteProvider:Yahoo:UserAgent is required.")]
    public void YahooOptions_WhenInvalid_FailValidation(string key, string value, string expectedError)
    {
        using var services = BuildProvider(new() { [key] = value });

        var exception = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<YahooFinanceOptions>>().Value);

        Assert.Contains(exception.Failures, failure => failure.Contains(expectedError, StringComparison.Ordinal));
    }

    [Fact]
    public void YahooOptions_AreValidatedOnStart()
    {
        using var services = BuildProvider(new() { ["QuoteProvider:Yahoo:UserAgent"] = "" });

        var exception = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains(exception.Failures, failure => failure.Contains("QuoteProvider:Yahoo:UserAgent is required.", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidConfiguration_PassesStartupValidation()
    {
        using var services = BuildProvider();

        services.GetRequiredService<IStartupValidator>().Validate();
    }

    [Fact]
    public void StartupValidation_ReportsEveryInvalidSectionAtOnce()
    {
        using var services = BuildProvider(new()
        {
            ["QuoteProvider:Yahoo:UserAgent"] = "",
            ["Email:Recipient"] = "",
            ["Smtp:Password"] = "",
        });

        var exception = Assert.Throws<AggregateException>(() => services.GetRequiredService<IStartupValidator>().Validate());

        var failures = exception.InnerExceptions.Cast<OptionsValidationException>().SelectMany(inner => inner.Failures).ToList();
        Assert.Contains(failures, failure => failure.Contains("QuoteProvider:Yahoo:UserAgent is required.", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains("Email:Recipient is required.", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains("Smtp:Password is required", StringComparison.Ordinal));
    }

    [Fact]
    public void EmailOptions_AreBoundFromConfiguration()
    {
        using var services = BuildProvider(new() { ["Email:SenderName"] = "Alertas B3" });

        var options = services.GetRequiredService<IOptions<EmailOptions>>().Value;

        Assert.Equal("dest@gmail.com", options.Recipient);
        Assert.Equal("me@gmail.com", options.SenderAddress);
        Assert.Equal("Alertas B3", options.SenderName);
    }

    [Fact]
    public void SmtpOptions_AreBoundFromConfiguration()
    {
        using var services = BuildProvider(new()
        {
            ["Smtp:Port"] = "465",
            ["Smtp:Security"] = "SslOnConnect",
            ["Smtp:Timeout"] = "00:01:00",
            ["Smtp:CheckCertificateRevocation"] = "true",
        });

        var options = services.GetRequiredService<IOptions<SmtpOptions>>().Value;

        Assert.Equal("smtp.gmail.com", options.Host);
        Assert.Equal(465, options.Port);
        Assert.Equal(SmtpSecurity.SslOnConnect, options.Security);
        Assert.True(options.RequireAuthentication);
        Assert.Equal("me@gmail.com", options.Username);
        Assert.Equal("app-password", options.Password);
        Assert.Equal(TimeSpan.FromMinutes(1), options.Timeout);
        Assert.True(options.CheckCertificateRevocation);
    }

    [Fact]
    public void SmtpOptions_WithoutAuthentication_AcceptLocalServerWithoutCredentials()
    {
        using var services = BuildProvider(new()
        {
            ["Smtp:Host"] = "localhost",
            ["Smtp:Port"] = "1025",
            ["Smtp:Security"] = "None",
            ["Smtp:RequireAuthentication"] = "false",
            ["Smtp:Username"] = "",
            ["Smtp:Password"] = "",
        });

        var options = services.GetRequiredService<IOptions<SmtpOptions>>().Value;

        Assert.False(options.RequireAuthentication);
        Assert.Equal(SmtpSecurity.None, options.Security);
    }

    [Theory]
    [InlineData("Email:Recipient", "", "Email:Recipient is required.")]
    [InlineData("Email:Recipient", "not-an-email", "Email:Recipient must be a valid e-mail address.")]
    [InlineData("Email:SenderAddress", "", "Email:SenderAddress is required.")]
    [InlineData("Email:SenderName", "", "Email:SenderName is required.")]
    public void EmailOptions_WhenInvalid_FailValidation(string key, string value, string expectedError)
    {
        using var services = BuildProvider(new() { [key] = value });

        var exception = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<EmailOptions>>().Value);

        Assert.Contains(exception.Failures, failure => failure.Contains(expectedError, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Smtp:Host", "", "Smtp:Host is required.")]
    [InlineData("Smtp:Port", "70000", "Smtp:Port must be between 1 and 65535.")]
    [InlineData("Smtp:Security", "7", "Smtp:Security must be StartTls, SslOnConnect or None.")]
    [InlineData("Smtp:Security", "None", "Smtp:Security None would send the password unencrypted")]
    [InlineData("Smtp:Username", "", "Smtp:Username is required when Smtp:RequireAuthentication is true.")]
    [InlineData("Smtp:Password", "", "Smtp:Password is required when Smtp:RequireAuthentication is true")]
    [InlineData("Smtp:Timeout", "00:10:00", "Smtp:Timeout must be between 1 second and 5 minutes.")]
    public void SmtpOptions_WhenInvalid_FailValidation(string key, string value, string expectedError)
    {
        using var services = BuildProvider(new() { [key] = value });

        var exception = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<SmtpOptions>>().Value);

        Assert.Contains(exception.Failures, failure => failure.Contains(expectedError, StringComparison.Ordinal));
    }

    [Fact]
    public void YahooClient_UsesConfiguredBaseAddressAndNeverRotatesItsHandler()
    {
        using var services = BuildProvider(new() { ["QuoteProvider:Yahoo:BaseUrl"] = "https://yahoo.test/" });

        var factoryOptions = services.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>().Get(DependencyInjection.YahooClientName);
        using var client = services.GetRequiredService<IHttpClientFactory>().CreateClient(DependencyInjection.YahooClientName);

        Assert.Equal(new Uri("https://yahoo.test/"), client.BaseAddress);
        Assert.Equal(Timeout.InfiniteTimeSpan, factoryOptions.HandlerLifetime);
    }

    [Fact]
    public void YahooClient_RecyclesPooledConnections()
    {
        using var services = BuildProvider(useFakeApi: false);

        var factoryOptions = services.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>().Get(DependencyInjection.YahooClientName);
        var builder = new TestHandlerBuilder(services);
        foreach (var configure in factoryOptions.HttpMessageHandlerBuilderActions)
        {
            configure(builder);
        }

        var handler = Assert.IsType<SocketsHttpHandler>(builder.PrimaryHandler);
        Assert.Equal(DependencyInjection.PooledConnectionLifetime, handler.PooledConnectionLifetime);
        handler.Dispose();
    }

    [Fact]
    public async Task QuoteProvider_FetchesFromConfiguredYahooUrlWithUserAgent()
    {
        using var services = BuildProvider(new()
        {
            ["QuoteProvider:Yahoo:BaseUrl"] = "https://yahoo.test/",
            ["QuoteProvider:Yahoo:UserAgent"] = "custom-agent/2.0",
        });
        _api.RespondWithPrices(22.6349m);

        var quote = await services.GetRequiredService<IQuoteProvider>().GetQuoteAsync(TestData.Petr4, Ct);

        Assert.Equal(new Quote(TestData.Petr4, 22.63m, TestData.Now, "Yahoo Finance"), quote);
        var request = Assert.Single(_api.Requests);
        Assert.Equal("https://yahoo.test/v8/finance/chart/PETR4.SA?interval=1d&range=1d", request.Uri.ToString());
        Assert.Equal("custom-agent/2.0", request.UserAgent);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task QuoteProvider_RetriesTransientFailures(HttpStatusCode statusCode)
    {
        using var services = BuildProvider();
        _api.RespondWithStatus(statusCode).RespondWithStatus(statusCode).RespondWithPrices(22.70m);

        var quote = await services.GetRequiredService<IQuoteProvider>().GetQuoteAsync(TestData.Petr4, Ct);

        Assert.Equal(22.70m, quote.Price);
        Assert.Equal(3, _api.Requests.Count);
    }

    [Fact]
    public async Task QuoteProvider_RetriesNetworkErrors()
    {
        using var services = BuildProvider();
        _api.FailWith(new HttpRequestException("Connection refused")).RespondWithPrices(22.70m);

        var quote = await services.GetRequiredService<IQuoteProvider>().GetQuoteAsync(TestData.Petr4, Ct);

        Assert.Equal(22.70m, quote.Price);
        Assert.Equal(2, _api.Requests.Count);
    }

    [Fact]
    public async Task QuoteProvider_WhenFailuresPersist_GivesUpWithRecoverableException()
    {
        using var services = BuildProvider();
        for (var attempt = 0; attempt < StandardAttempts; attempt++)
        {
            _api.RespondWithStatus(HttpStatusCode.ServiceUnavailable);
        }

        var exception = await Assert.ThrowsAsync<QuoteProviderException>(
            () => services.GetRequiredService<IQuoteProvider>().GetQuoteAsync(TestData.Petr4, Ct));

        Assert.True(exception.IsRecoverable);
        Assert.Equal("Yahoo Finance responded with HTTP 503 for PETR4.", exception.Message);
        Assert.Equal(StandardAttempts, _api.Requests.Count);
    }

    [Fact]
    public async Task QuoteProvider_DoesNotRetryUnknownTicker()
    {
        using var services = BuildProvider();
        _api.RespondWithStatus(HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<TickerNotFoundException>(
            () => services.GetRequiredService<IQuoteProvider>().GetQuoteAsync(TestData.Petr4, Ct));

        Assert.Single(_api.Requests);
    }

    [Fact]
    public async Task QuoteProvider_WhenEveryAttemptTimesOut_ThrowsRecoverableException()
    {
        using var services = BuildProvider(attemptTimeout: TimeSpan.FromMilliseconds(50));
        for (var attempt = 0; attempt < StandardAttempts; attempt++)
        {
            _api.RespondSlowly(TimeSpan.FromSeconds(30));
        }

        var exception = await Assert.ThrowsAsync<QuoteProviderException>(
            () => services.GetRequiredService<IQuoteProvider>().GetQuoteAsync(TestData.Petr4, Ct));

        Assert.True(exception.IsRecoverable);
        Assert.StartsWith("Yahoo Finance is unavailable for PETR4:", exception.Message, StringComparison.Ordinal);
        Assert.Equal(StandardAttempts, _api.Requests.Count);
    }

    [Fact]
    public async Task QuoteProvider_WhenOneAttemptTimesOut_RetriesAndSucceeds()
    {
        using var services = BuildProvider(attemptTimeout: TimeSpan.FromMilliseconds(50));
        _api.RespondSlowly(TimeSpan.FromSeconds(30)).RespondWithPrices(22.70m);

        var quote = await services.GetRequiredService<IQuoteProvider>().GetQuoteAsync(TestData.Petr4, Ct);

        Assert.Equal(22.70m, quote.Price);
        Assert.Equal(2, _api.Requests.Count);
    }

    public void Dispose() => _api.Dispose();

    private ServiceProvider BuildProvider(
        Dictionary<string, string?>? overrides = null,
        bool useFakeApi = true,
        TimeSpan? attemptTimeout = null)
    {
        var settings = new Dictionary<string, string?>(ValidSettings);

        foreach (var (key, value) in overrides ?? [])
        {
            settings[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddLogging()
            .AddInfrastructure();

        if (useFakeApi)
        {
            services.AddHttpClient(DependencyInjection.YahooClientName).ConfigurePrimaryHttpMessageHandler(() => _api);
            services.Configure<HttpStandardResilienceOptions>(YahooPipelineName(), options =>
            {
                options.Retry.Delay = TimeSpan.Zero;

                if (attemptTimeout is { } timeout)
                {
                    options.AttemptTimeout.Timeout = timeout;
                }
            });
        }

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    /// <summary>Asks the library for the name it gives the Yahoo client's standard pipeline.</summary>
    private static string YahooPipelineName() =>
        new ServiceCollection().AddHttpClient(DependencyInjection.YahooClientName).AddStandardResilienceHandler().PipelineName;

    private sealed class TestHandlerBuilder(IServiceProvider services) : HttpMessageHandlerBuilder
    {
        public override string? Name { get; set; } = DependencyInjection.YahooClientName;

        public override HttpMessageHandler PrimaryHandler { get; set; } = new HttpClientHandler();

        public override IList<DelegatingHandler> AdditionalHandlers { get; } = [];

        public override IServiceProvider Services => services;

        public override HttpMessageHandler Build() => PrimaryHandler;
    }
}