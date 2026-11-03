using StockQuoteAlert.Core.Application.Journal;
using StockQuoteAlert.Core.Application.Monitoring;

namespace StockQuoteAlert.Cli.Components.Dashboard;

/// <summary>What the dashboard shows about one monitored stock, derived from the journal snapshot shared by all stocks.</summary>
public sealed record TickerDashboard(
    MonitoringTarget Target,
    IReadOnlyList<QuoteObservation> Quotes,
    IReadOnlyList<NotificationRecord> Notifications,
    MonitoringFailure? ActiveFailure)
{
    public string Symbol => Target.Ticker.Symbol;

    public QuoteObservation? LastQuote => Quotes.Count > 0 ? Quotes[^1] : null;

    public MonitoringHealth Health => DashboardText.Health(ActiveFailure);

    public int Sent => Notifications.Count(notification => notification.Status == NotificationStatus.Sent);

    public int Failed => Notifications.Count - Sent;

    public static IReadOnlyList<TickerDashboard> Create(IEnumerable<MonitoringTarget> targets, MonitoringSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(snapshot);

        var quotes = snapshot.Quotes.ToLookup(observation => observation.Quote.Ticker);
        var notifications = snapshot.Notifications.ToLookup(notification => notification.Alert.Ticker);
        var failures = snapshot.LastFailures.ToDictionary(failure => failure.Ticker);

        return [.. targets.Select(target =>
        {
            QuoteObservation[] tickerQuotes = [.. quotes[target.Ticker]];
            var activeFailure = Active(failures.GetValueOrDefault(target.Ticker), tickerQuotes.LastOrDefault());

            return new TickerDashboard(target, tickerQuotes, [.. notifications[target.Ticker]], activeFailure);
        })];
    }

    /// <summary>Rebuilds the data of a single stock, so a journal event for one stock leaves the others untouched.</summary>
    public static TickerDashboard For(MonitoringTarget target, MonitoringSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(snapshot);

        QuoteObservation[] quotes = [.. snapshot.Quotes.Where(observation => observation.Quote.Ticker == target.Ticker)];
        NotificationRecord[] notifications = [.. snapshot.Notifications.Where(notification => notification.Alert.Ticker == target.Ticker)];
        var failure = snapshot.LastFailures.FirstOrDefault(failure => failure.Ticker == target.Ticker);

        return new TickerDashboard(target, quotes, notifications, Active(failure, quotes.LastOrDefault()));
    }

    /// <summary>A failure stays relevant until a newer quote of the same stock proves its monitoring has recovered.</summary>
    internal static MonitoringFailure? Active(MonitoringFailure? failure, QuoteObservation? lastQuote) => failure switch
    {
        null => null,
        { IsRecoverable: false } => failure,
        _ when lastQuote is null || failure.OccurredAt > lastQuote.ObservedAt => failure,
        _ => null,
    };
}