using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using StockQuoteAlert.Core.Application.Abstractions;
using StockQuoteAlert.Core.Application.Journal;
using StockQuoteAlert.Core.Application.Monitoring;
using StockQuoteAlert.Core.Domain;
using StockQuoteAlert.Infrastructure.Journal;
using StockQuoteAlert.Infrastructure.Quotes;

namespace StockQuoteAlert.Tests.TestDoubles;

/// <summary>
/// bUnit context for the dashboard: the real in-memory journal and the services the page injects; only the clock is
/// faked. Renders as an interactive (Blazor Server) circuit unless told otherwise.
/// </summary>
public abstract class DashboardTestContext : BunitContext
{
    protected DashboardTestContext()
    {
        Services.AddSingleton<IMonitoringJournal>(Journal);
        Services.AddSingleton(_ => Targets);
        Services.AddSingleton<TimeProvider>(Time);
        Services.AddSingleton(_ => Options.Create(MonitoringOptions));
        Services.AddSingleton(_ => Options.Create(QuoteProviderOptions));
        Services.AddSingleton(_ => Options.Create(TestData.EmailOptions));
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("Server", isInteractive: true));
    }

    internal InMemoryMonitoringJournal Journal { get; } = new();

    internal FakeTimeProvider Time { get; } = new(TestData.Now);

    internal MonitoringTargets Targets { get; set; } = TestData.Targets;

    internal MonitoringOptions MonitoringOptions { get; } = new() { PollingInterval = TimeSpan.FromSeconds(30) };

    internal QuoteProviderOptions QuoteProviderOptions { get; } = new();

    internal QuoteObservation RecordQuote(decimal price, Ticker? ticker = null, int secondsAfterNow = 0)
    {
        var symbol = ticker ?? TestData.Petr4;
        var thresholds = Targets.First(target => target.Ticker == symbol).Thresholds;
        var observation = new QuoteObservation(
            new Quote(symbol, price, TestData.Now, "Yahoo Finance"),
            thresholds.Classify(price),
            TestData.Now.AddSeconds(secondsAfterNow));

        Journal.RecordQuote(observation);
        return observation;
    }

    internal NotificationRecord RecordNotification(PriceAlert alert, NotificationStatus status = NotificationStatus.Sent, string? error = null, int secondsAfterNow = 0)
    {
        var notification = new NotificationRecord(
            Guid.CreateVersion7(),
            alert,
            "E-mail",
            "dest@gmail.com",
            $"[{(alert.Kind == AlertKind.Sell ? "VENDA" : "COMPRA")}] {alert.Ticker} alerta",
            "Olá,\n\ncorpo do e-mail",
            TestData.Now.AddSeconds(secondsAfterNow),
            status,
            error);

        Journal.RecordNotification(notification);
        return notification;
    }

    internal MonitoringFailure RecordFailure(MonitoringFailureKind kind, Ticker? ticker = null, string reason = "Yahoo Finance responded with HTTP 503 for PETR4.", int secondsAfterNow = 0)
    {
        var failure = new MonitoringFailure(ticker ?? TestData.Petr4, TestData.Now.AddSeconds(secondsAfterNow), reason, kind);

        Journal.RecordFailure(failure);
        return failure;
    }
}