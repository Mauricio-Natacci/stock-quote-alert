using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using StockQuoteAlert.Core.Application.Abstractions;
using StockQuoteAlert.Core.Application.Monitoring;
using StockQuoteAlert.Infrastructure.Email;
using StockQuoteAlert.Infrastructure.Quotes;

namespace StockQuoteAlert.Cli.Dashboard;

internal static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardApi(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api");

        api.MapGet("/health", () => TypedResults.Ok(new HealthResponse("healthy")));
        api.MapGet("/dashboard", GetSnapshot);

        return endpoints;
    }

    internal static Ok<DashboardSnapshotResponse> GetSnapshot(
        IMonitoringJournal journal,
        MonitoringTargets targets,
        MonitoringSession session,
        IOptions<MonitoringOptions> monitoringOptions,
        IOptions<QuoteProviderOptions> quoteProviderOptions,
        IOptions<EmailOptions> emailOptions)
    {
        var snapshot = journal.GetSnapshot();

        var configuration = new MonitoringConfigurationResponse(
            [.. targets.Select(TargetResponse.From)],
            quoteProviderOptions.Value.Provider.DisplayName(),
            quoteProviderOptions.Value.Fallback?.DisplayName(),
            monitoringOptions.Value.PollingInterval.TotalSeconds,
            emailOptions.Value.Recipient,
            session.StartedAt);

        return TypedResults.Ok(new DashboardSnapshotResponse(
            configuration,
            [.. snapshot.Quotes.Select(QuoteResponse.From)],
            [.. snapshot.Notifications.Select(NotificationResponse.From)],
            [.. snapshot.LastFailures.Select(FailureResponse.From)]));
    }
}

/// <summary>Body of <c>GET /api/health</c>.</summary>
internal sealed record HealthResponse(string Status);