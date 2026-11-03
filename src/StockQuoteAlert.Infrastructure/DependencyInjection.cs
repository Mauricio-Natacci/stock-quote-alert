using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StockQuoteAlert.Core.Application.Abstractions;
using StockQuoteAlert.Infrastructure.Email;
using StockQuoteAlert.Infrastructure.Quotes;
using StockQuoteAlert.Infrastructure.Quotes.Yahoo;

namespace StockQuoteAlert.Infrastructure;

public static class DependencyInjection
{
    internal const string YahooClientName = nameof(YahooFinanceQuoteSource);

    internal static readonly TimeSpan PooledConnectionLifetime = TimeSpan.FromMinutes(5);

    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services
            .AddQuoteProvider()
            .AddEmailNotifications();
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

    private static IServiceCollection AddQuoteProvider(this IServiceCollection services)
    {
        services.AddValidatedOptions<YahooFinanceOptions>(YahooFinanceOptions.SectionName);

        services
            .AddHttpClient<YahooFinanceQuoteSource>(YahooClientName, (provider, client) =>
                client.BaseAddress = provider.GetRequiredService<IOptions<YahooFinanceOptions>>().Value.BaseUrl)
            .ConfigureForLongLivedUsage()
            .AddStandardResilienceHandler();

        services.AddSingleton<IQuoteProvider>(provider =>
            new QuoteSourceAdapter(provider.GetRequiredService<YahooFinanceQuoteSource>()));

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
        services.AddSingleton<IAlertNotifier, EmailAlertNotifier>();

        return services;
    }

    // The quote client is captured by a singleton, so connections are recycled by the
    // SocketsHttpHandler instead of the factory's handler rotation (avoids stale DNS).
    private static IHttpClientBuilder ConfigureForLongLivedUsage(this IHttpClientBuilder builder) =>
        builder
            .UseSocketsHttpHandler((handler, _) => handler.PooledConnectionLifetime = PooledConnectionLifetime)
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
}