using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using NSubstitute;
using StockQuoteAlert.Cli;
using StockQuoteAlert.Cli.CommandLine;
using StockQuoteAlert.Cli.Logging;
using StockQuoteAlert.Core.Application.Abstractions;
using StockQuoteAlert.Core.Application.Monitoring;
using StockQuoteAlert.Infrastructure.Email;
using StockQuoteAlert.Infrastructure.Quotes;
using StockQuoteAlert.Infrastructure.Quotes.Brapi;
using StockQuoteAlert.Infrastructure.Quotes.Yahoo;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Cli;

/// <summary>Checks the shipped configuration files and how the host loads them.</summary>
public sealed class AppConfigurationTests
{
    private static readonly string AppSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    [Fact]
    public void Assembly_HasUserSecretsId()
    {
        var attribute = typeof(ExitCodes).Assembly.GetCustomAttribute<Microsoft.Extensions.Configuration.UserSecrets.UserSecretsIdAttribute>();

        Assert.Equal("stock-quote-alert-6f1c2a9e", attribute?.UserSecretsId);
    }

    [Fact]
    public void BuilderSettings_UseTheExecutableNameAndFolder()
    {
        var settings = StockQuoteAlertApp.BuilderSettings();

        Assert.Equal("stock-quote-alert", settings.ApplicationName);
        Assert.Equal(AppContext.BaseDirectory, settings.ContentRootPath);
    }

    [Fact]
    public void InDevelopment_UserSecretsAreLoaded()
    {
        Assert.Contains(JsonFiles(environment: Environments.Development), path => path == "secrets.json");
    }

    [Fact]
    public void InProduction_UserSecretsAreNotLoaded()
    {
        Assert.DoesNotContain(JsonFiles(environment: Environments.Production), path => path == "secrets.json");
    }

    [Fact]
    public void Create_LoadsAppSettingsFromTheExecutableFolder()
    {
        using var host = StockQuoteAlertApp.Create(TestData.Targets);

        var configuration = host.Services.GetRequiredService<IConfiguration>();
        Assert.Equal("smtp.gmail.com", configuration["Smtp:Host"]);
        Assert.Equal("00:00:30", configuration["Monitoring:PollingInterval"]);
        Assert.Equal("https://query1.finance.yahoo.com/", configuration["QuoteProvider:Yahoo:BaseUrl"]);
    }

    [Fact]
    public void AppSettings_IsValidJson()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(AppSettingsPath));

        Assert.Equal(
            ["Email", "Smtp", "QuoteProvider", "Monitoring", "Logging"],
            document.RootElement.EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public void AppSettings_MatchTheOptionDefaults()
    {
        var configuration = new ConfigurationBuilder().AddJsonFile(AppSettingsPath).Build();

        var email = Bind<EmailOptions>(configuration, EmailOptions.SectionName);
        var smtp = Bind<SmtpOptions>(configuration, SmtpOptions.SectionName);
        var yahoo = Bind<YahooFinanceOptions>(configuration, YahooFinanceOptions.SectionName);
        var monitoring = Bind<MonitoringOptions>(configuration, MonitoringOptions.SectionName);

        Assert.Equal(new EmailOptions().SenderName, email.SenderName);
        Assert.Equal("smtp.gmail.com", smtp.Host);
        Assert.Equal(new SmtpOptions().Port, smtp.Port);
        Assert.Equal(new SmtpOptions().Security, smtp.Security);
        Assert.Equal(new SmtpOptions().RequireAuthentication, smtp.RequireAuthentication);
        Assert.Equal(new SmtpOptions().CheckCertificateRevocation, smtp.CheckCertificateRevocation);
        Assert.Equal(new SmtpOptions().Timeout, smtp.Timeout);
        Assert.Equal(new YahooFinanceOptions().BaseUrl, yahoo.BaseUrl);
        Assert.Equal(new YahooFinanceOptions().UserAgent, yahoo.UserAgent);
        Assert.Equal(new MonitoringOptions().PollingInterval, monitoring.PollingInterval);

        var providers = Bind<QuoteProviderOptions>(configuration, QuoteProviderOptions.SectionName);
        var brapi = Bind<BrapiOptions>(configuration, BrapiOptions.SectionName);
        Assert.Equal(QuoteProviderKind.Yahoo, providers.Provider);
        Assert.Equal(QuoteProviderKind.Brapi, providers.Fallback);
        Assert.Equal(new BrapiOptions().BaseUrl, brapi.BaseUrl);
    }

    [Fact]
    public void AppSettings_KeepsAddressesAndCredentialsEmpty()
    {
        var configuration = new ConfigurationBuilder().AddJsonFile(AppSettingsPath).Build();

        Assert.Equal("", configuration["Email:Recipient"]);
        Assert.Equal("", configuration["Email:SenderAddress"]);
        Assert.Equal("", configuration["Smtp:Username"]);
        Assert.Equal("", configuration["Smtp:Password"]);
        Assert.Equal("", configuration["QuoteProvider:Brapi:Token"]);
    }

    [Fact]
    public void AppSettings_AsShipped_AsksOnlyForAddressesAndCredentials()
    {
        using var host = StockQuoteAlertApp.Create(TestData.Targets);

        var exception = Assert.Throws<AggregateException>(() => host.Services.GetRequiredService<IStartupValidator>().Validate());

        var failures = exception.InnerExceptions.Cast<OptionsValidationException>().SelectMany(inner => inner.Failures).ToList();
        Assert.Equal(4, failures.Count);
        Assert.Contains(failures, failure => failure.Contains("Email:Recipient is required.", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains("Email:SenderAddress is required.", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains("Smtp:Username is required", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains("Smtp:Password is required", StringComparison.Ordinal));
    }

    [Fact]
    public void Create_WithShippedSettings_UsesYahooWithBrapiFallback()
    {
        using var host = StockQuoteAlertApp.Create(TestData.Targets, builder => builder.Configuration.AddInMemoryCollection(AppSettings.Valid));

        Assert.IsType<FallbackQuoteProvider>(host.Services.GetRequiredService<IQuoteProvider>());
    }

    [Fact]
    public void AppSettings_WithAddressesAndCredentials_PassesStartupValidation()
    {
        using var host = StockQuoteAlertApp.Create(TestData.Targets, builder => builder.Configuration.AddInMemoryCollection(AppSettings.Valid));

        host.Services.GetRequiredService<IStartupValidator>().Validate();
    }

    [Fact]
    public void Create_UsesTheCompactConsoleFormatter()
    {
        using var host = StockQuoteAlertApp.Create(TestData.Targets);

        Assert.Equal(CompactConsoleFormatter.FormatterName, host.Services.GetRequiredService<IOptions<ConsoleLoggerOptions>>().Value.FormatterName);
        Assert.Contains(host.Services.GetServices<ConsoleFormatter>(), formatter => formatter is CompactConsoleFormatter);
        Assert.Equal("HH:mm:ss ", host.Services.GetRequiredService<IOptionsMonitor<CompactConsoleFormatterOptions>>().CurrentValue.TimestampFormat);
    }

    [Fact]
    public async Task Run_DoesNotPrintTheHostStartupBanner()
    {
        var logs = new CapturingLoggerProvider();
        using var host = StockQuoteAlertApp.Create(TestData.Targets, builder =>
        {
            builder.Configuration.AddInMemoryCollection(AppSettings.Valid);
            builder.Logging.AddProvider(logs);
            builder.Services.AddSingleton(Substitute.For<IQuoteProvider>());
        });

        await host.StartAsync(TestContext.Current.CancellationToken);
        await Eventually.HoldsAsync(() => logs.Entries.Any(entry => entry.Message.StartsWith("Monitoring PETR4", StringComparison.Ordinal)));
        await host.StopAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain(logs.Entries, entry => entry.Category == "Microsoft.Hosting.Lifetime");
        Assert.DoesNotContain(logs.Entries, entry => entry.Message.Contains("Press Ctrl+C", StringComparison.Ordinal));
    }

    [Fact]
    public void LaunchSettings_RunsAValidCommandInDevelopment()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindLaunchSettings()));
        var profile = document.RootElement.GetProperty("profiles").GetProperty("stock-quote-alert");

        Assert.Equal("Project", profile.GetProperty("commandName").GetString());
        Assert.Equal(Environments.Development, profile.GetProperty("environmentVariables").GetProperty("DOTNET_ENVIRONMENT").GetString());

        var args = profile.GetProperty("commandLineArgs").GetString()!.Split(' ');
        Assert.IsType<CommandLineResult.Success>(CommandLineParser.Parse(args));
    }

    private static T Bind<T>(IConfiguration configuration, string section)
        where T : new()
    {
        var options = new T();
        configuration.GetSection(section).Bind(options);
        return options;
    }

    private static List<string?> JsonFiles(string environment)
    {
        var settings = StockQuoteAlertApp.BuilderSettings();
        settings.EnvironmentName = environment;
        var builder = Host.CreateApplicationBuilder(settings);

        return [.. ((IConfigurationRoot)builder.Configuration).Providers
            .OfType<JsonConfigurationProvider>()
            .Select(provider => provider.Source.Path)];
    }

    private static string FindLaunchSettings()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "StockQuoteAlert.Cli", "Properties", "launchSettings.json");

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("launchSettings.json not found above the test output folder.");
    }
}