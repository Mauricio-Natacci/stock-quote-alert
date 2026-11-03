using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StockQuoteAlert.Core.Application.Abstractions;
using StockQuoteAlert.Infrastructure.Email;
using StockQuoteAlert.Infrastructure.Journal;
using StockQuoteAlert.Infrastructure.Messaging;
using StockQuoteAlert.Infrastructure.Quotes;
using StockQuoteAlert.Infrastructure.Quotes.Brapi;
using StockQuoteAlert.Infrastructure.Quotes.Yahoo;
using StockQuoteAlert.Infrastructure.Slack;
using StockQuoteAlert.Infrastructure.Sms;

namespace StockQuoteAlert.Infrastructure;

public static class DependencyInjection
{
    internal const string YahooClientName = nameof(YahooFinanceQuoteSource);

    internal const string BrapiClientName = nameof(BrapiQuoteSource);

    internal static readonly TimeSpan PooledConnectionLifetime = TimeSpan.FromMinutes(5);

    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IMonitoringJournal, InMemoryMonitoringJournal>();

        return services
            .AddQuoteProviders()
            .AddAlertMessaging()
            .AddEmailNotifications()
            .AddExampleConsumers();
    }

    /// <summary>
    /// Binds <typeparamref name="TOptions"/> to a configuration section and validates its DataAnnotations (and
    /// <see cref="System.ComponentModel.DataAnnotations.IValidatableObject"/>) when the host starts, so a bad
    /// configuration stops the app before any work begins.
    /// </summary>
    public static IServiceCollection AddValidatedOptions<TOptions>(this IServiceCollection services, string sectionName)
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);

        services.AddOptions<TOptions>()
            .BindConfiguration(sectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }

    private static IServiceCollection AddQuoteProviders(this IServiceCollection services)
    {
        services
            .AddValidatedOptions<QuoteProviderOptions>(QuoteProviderOptions.SectionName)
            .AddValidatedOptions<YahooFinanceOptions>(YahooFinanceOptions.SectionName)
            .AddValidatedOptions<BrapiOptions>(BrapiOptions.SectionName);

        services.AddTransient<BrapiAuthenticationHandler>();

        services
            .AddHttpClient<YahooFinanceQuoteSource>(YahooClientName, (provider, client) =>
                client.BaseAddress = provider.GetRequiredService<IOptions<YahooFinanceOptions>>().Value.BaseUrl)
            .ConfigureForLongLivedUsage()
            .AddStandardResilienceHandler();

        services
            .AddHttpClient<BrapiQuoteSource>(BrapiClientName, (provider, client) =>
                client.BaseAddress = provider.GetRequiredService<IOptions<BrapiOptions>>().Value.BaseUrl)
            .ConfigureForLongLivedUsage()
            .AddHttpMessageHandler<BrapiAuthenticationHandler>()
            .AddStandardResilienceHandler();

        services.AddSingleton<IQuoteProvider>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<QuoteProviderOptions>>().Value;
            var primary = CreateAdapter(provider, options.Provider);

            return options.Fallback is { } fallback
                ? new FallbackQuoteProvider(primary, CreateAdapter(provider, fallback), provider.GetRequiredService<ILogger<FallbackQuoteProvider>>())
                : primary;
        });

        return services;
    }

    private static QuoteSourceAdapter CreateAdapter(IServiceProvider provider, QuoteProviderKind kind) =>
        new(kind switch
        {
            QuoteProviderKind.Yahoo => provider.GetRequiredService<YahooFinanceQuoteSource>(),
            QuoteProviderKind.Brapi => provider.GetRequiredService<BrapiQuoteSource>(),
            _ => throw new InvalidOperationException($"Unsupported quote provider '{kind}'."),
        });

    private static IServiceCollection AddAlertMessaging(this IServiceCollection services)
    {
        services.AddSingleton<InMemoryAlertQueue>();
        services.AddSingleton<IAlertPublisher>(provider => provider.GetRequiredService<InMemoryAlertQueue>());
        services.AddHostedService<AlertDispatcher>();

        return services;
    }

    private static IServiceCollection AddEmailNotifications(this IServiceCollection services)
    {
        services
            .AddValidatedOptions<EmailOptions>(EmailOptions.SectionName)
            .AddValidatedOptions<SmtpOptions>(SmtpOptions.SectionName);

        services.AddSingleton<IAlertEmailComposer, AlertEmailComposer>();
        services.AddSingleton<ISmtpClientFactory, SmtpClientFactory>();
        services.AddSingleton<IEmailSender, MailKitEmailSender>();
        services.AddSingleton<IAlertConsumer, EmailAlertConsumer>();

        return services;
    }

    // Examples of other channels: they show where an integration plugs in, and only log.
    private static IServiceCollection AddExampleConsumers(this IServiceCollection services)
    {
        services.AddSingleton<IAlertConsumer, SlackAlertConsumer>();
        services.AddSingleton<IAlertConsumer, SmsAlertConsumer>();

        return services;
    }

    // Quote clients are captured by a singleton, so connections are recycled by the
    // SocketsHttpHandler instead of the factory's handler rotation (avoids stale DNS).
    private static IHttpClientBuilder ConfigureForLongLivedUsage(this IHttpClientBuilder builder) =>
        builder
            .UseSocketsHttpHandler((handler, _) => handler.PooledConnectionLifetime = PooledConnectionLifetime)
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
}