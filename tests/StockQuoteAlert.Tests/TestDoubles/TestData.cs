using StockQuoteAlert.Core.Application.Alerts;
using StockQuoteAlert.Core.Application.Monitoring;
using StockQuoteAlert.Core.Domain;
using StockQuoteAlert.Infrastructure.Email;

namespace StockQuoteAlert.Tests.TestDoubles;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 29, 17, 30, 0, TimeSpan.Zero);

    public static Ticker Petr4 { get; } = Ticker.Create("PETR4");

    public static AlertThresholds Thresholds { get; } = new(sellAbove: 22.67m, buyBelow: 22.59m);

    public static MonitoringTarget Target { get; } = new(Petr4, Thresholds);

    public static Ticker Vale3 { get; } = Ticker.Create("VALE3");

    public static AlertThresholds ValeThresholds { get; } = new(sellAbove: 61.50m, buyBelow: 60.80m);

    public static MonitoringTarget ValeTarget { get; } = new(Vale3, ValeThresholds);

    /// <summary>PETR4 alone.</summary>
    public static MonitoringTargets Targets { get; } = new([Target]);

    public static MonitoringTargets PetrAndVale { get; } = new([Target, ValeTarget]);

    public static EmailOptions EmailOptions => new()
    {
        Recipient = "dest@gmail.com",
        SenderAddress = "me@gmail.com",
        SenderName = "Stock Quote Alert",
    };

    public static SmtpOptions SmtpOptions => new()
    {
        Host = "smtp.gmail.com",
        Port = 587,
        Security = SmtpSecurity.StartTls,
        Username = "me@gmail.com",
        Password = "app-password",
        Timeout = TimeSpan.FromSeconds(15),
    };

    public static Quote QuoteAt(decimal price) => new(Petr4, price, Now, "Yahoo Finance");

    public static PriceAlert SellAlert(decimal price = 22.70m) => new(Petr4, AlertKind.Sell, price, Thresholds.SellAbove, Now);

    public static PriceAlert BuyAlert(decimal price = 22.50m) => new(Petr4, AlertKind.Buy, price, Thresholds.BuyBelow, Now);

    public static PriceAlertRaised Raised(PriceAlert alert) => new(Guid.CreateVersion7(), alert, Now);
}