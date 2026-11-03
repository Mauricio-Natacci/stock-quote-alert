using StockQuoteAlert.Core.Application.Journal;
using StockQuoteAlert.Core.Domain;
using StockQuoteAlert.Core.Formatting;
using StockQuoteAlert.Infrastructure.Quotes;

namespace StockQuoteAlert.Cli.Components.Dashboard;

internal static class DashboardText
{
    public static string Provider(QuoteProviderOptions options) =>
        options.Fallback is { } fallback
            ? $"{options.Provider.DisplayName()} (reserva: {fallback.DisplayName()})"
            : options.Provider.DisplayName();

    public static string Zone(PriceZone zone) => zone switch
    {
        PriceZone.AboveSellThreshold => "Acima da linha de venda",
        PriceZone.BelowBuyThreshold => "Abaixo da linha de compra",
        _ => "Na zona neutra",
    };

    /// <summary>
    /// How far the price is from the line that matters: past a crossed line, or from the nearest line while neutral.
    /// </summary>
    public static string Distance(decimal price, AlertThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(thresholds);

        return thresholds.Classify(price) switch
        {
            PriceZone.AboveSellThreshold => $"{Gap(price, thresholds.SellAbove)} acima da linha de venda",
            PriceZone.BelowBuyThreshold => $"{Gap(price, thresholds.BuyBelow)} abaixo da linha de compra",
            _ when thresholds.SellAbove - price <= price - thresholds.BuyBelow =>
                $"faltam {Gap(price, thresholds.SellAbove)} para a linha de venda",
            _ => $"faltam {Gap(price, thresholds.BuyBelow)} para a linha de compra",
        };
    }

    /// <summary>Signed variation of the alert price relative to the line it crossed (e.g. "+0,13%").</summary>
    public static string Deviation(PriceAlert alert)
    {
        ArgumentNullException.ThrowIfNull(alert);

        var percent = (alert.CurrentPrice - alert.ReferencePrice) / alert.ReferencePrice * 100m;
        return $"{(percent >= 0 ? "+" : "−")}{BrazilianFormat.Percent(Math.Abs(percent))}";
    }

    public static string Plural(int count, string singular, string plural) => count == 1 ? singular : plural;

    public static MonitoringHealth Health(MonitoringFailure? activeFailure) => activeFailure switch
    {
        null => MonitoringHealth.Live,
        { IsRecoverable: true } => MonitoringHealth.Unstable,
        _ => MonitoringHealth.Stopped,
    };

    /// <summary>Stopped only when no stock is monitored anymore; unstable while any stock has a problem.</summary>
    public static MonitoringHealth OverallHealth(IReadOnlyCollection<TickerDashboard> tickers)
    {
        ArgumentNullException.ThrowIfNull(tickers);

        return tickers switch
        {
            _ when tickers.All(ticker => ticker.Health == MonitoringHealth.Stopped) => MonitoringHealth.Stopped,
            _ when tickers.Any(ticker => ticker.Health != MonitoringHealth.Live) => MonitoringHealth.Unstable,
            _ => MonitoringHealth.Live,
        };
    }

    public static string ConnectionState(MonitoringHealth health) => health switch
    {
        MonitoringHealth.Live => "Ao vivo",
        MonitoringHealth.Unstable => "Instável",
        _ => "Interrompido",
    };

    public static string ConnectionModifier(MonitoringHealth health) => health switch
    {
        MonitoringHealth.Live => "live",
        MonitoringHealth.Unstable => "warning",
        _ => "stopped",
    };

    /// <summary>Plain-language explanation of failures of the same kind; the technical reason is shown apart.</summary>
    public static string FailureSummary(MonitoringFailureKind kind, IReadOnlyList<string> tickers)
    {
        ArgumentNullException.ThrowIfNull(tickers);

        var names = Names(tickers);
        var several = tickers.Count > 1;

        return kind switch
        {
            MonitoringFailureKind.Transient =>
                $"Não foi possível obter a cotação de {names} agora. Uma nova tentativa é feita a cada verificação.",
            MonitoringFailureKind.TickerNotFound =>
                $"{names} não {(several ? "foram encontrados" : "foi encontrado")} pelos provedores de cotação. " +
                $"Confira se {(several ? "os códigos estão corretos" : "o código está correto")} e reinicie o monitoramento.",
            _ =>
                $"O provedor de cotações recusou a consulta de {names}. Confira a configuração do provedor nos detalhes técnicos.",
        };
    }

    /// <summary>Joins names the Portuguese way: "A", "A e B", "A, B e C".</summary>
    public static string Names(IReadOnlyList<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        return names.Count <= 1 ? string.Concat(names) : $"{string.Join(", ", names.Take(names.Count - 1))} e {names[^1]}";
    }

    public static string Assets(int count) => $"{count} {Plural(count, "ativo", "ativos")}";

    public static string ZoneIcon(PriceZone zone) => zone switch
    {
        PriceZone.AboveSellThreshold => "▲",
        PriceZone.BelowBuyThreshold => "▼",
        _ => "●",
    };

    public static string Action(AlertKind kind) => kind == AlertKind.Sell ? "Venda" : "Compra";

    public static string ActionIcon(AlertKind kind) => kind == AlertKind.Sell ? "▲" : "▼";

    public static string Status(NotificationStatus status) => status == NotificationStatus.Sent ? "Enviado" : "Falhou";

    public static string StatusIcon(NotificationStatus status) => status == NotificationStatus.Sent ? "✓" : "✕";

    public static string Interval(TimeSpan interval) =>
        interval.TotalSeconds % 60 == 0 ? $"{interval.TotalMinutes:0} min" : $"{interval.TotalSeconds:0} s";

    public static string Relative(DateTimeOffset value, DateTimeOffset now)
    {
        var seconds = Math.Max(0, (int)(now - value).TotalSeconds);

        return seconds switch
        {
            < 60 => $"há {seconds} s",
            < 3600 => $"há {seconds / 60} min",
            _ => $"há {seconds / 3600} h",
        };
    }

    private static string Gap(decimal price, decimal reference)
    {
        var gap = Math.Abs(price - reference);
        return $"{BrazilianFormat.Currency(gap)} ({BrazilianFormat.Percent(gap / reference * 100m)})";
    }
}