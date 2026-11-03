using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using StockQuoteAlert.Core.Application.Abstractions;
using StockQuoteAlert.Core.Application.Alerts;
using StockQuoteAlert.Core.Application.Journal;
using StockQuoteAlert.Infrastructure.Messaging;
using StockQuoteAlert.Tests.TestDoubles;

namespace StockQuoteAlert.Tests.Infrastructure.Messaging;

public sealed class AlertDispatcherTests
{
    private static readonly TimeSpan Step = TimeSpan.FromSeconds(1);

    private readonly FakeTimeProvider _time = new(TestData.Now);
    private readonly InMemoryAlertQueue _queue = new();
    private readonly FakeLogger<AlertDispatcher> _log = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void RetryPolicy_IsFourAttemptsStartingAtFiveSeconds()
    {
        Assert.Equal(4, AlertDispatcher.MaxAttempts);
        Assert.Equal(TimeSpan.FromSeconds(5), AlertDispatcher.FirstRetryDelay);
    }

    [Fact]
    public async Task Dispatch_DeliversTheSameAlertToEveryConsumer()
    {
        var email = new FakeConsumer("E-mail", _time);
        var slack = new FakeConsumer("Slack", _time);
        using var dispatcher = CreateDispatcher(email, slack);
        var alert = TestData.Raised(TestData.SellAlert());

        await dispatcher.DispatchAsync(alert, Ct);

        Assert.Same(alert, Assert.Single(email.Received));
        Assert.Same(alert, Assert.Single(slack.Received));
        Assert.Empty(_log.Entries);
    }

    [Fact]
    public async Task Dispatch_DeliversToConsumersInParallel()
    {
        var slow = new FakeConsumer("Slow", _time, holdFor: TimeSpan.FromMinutes(5));
        var fast = new FakeConsumer("Fast", _time);
        using var dispatcher = CreateDispatcher(slow, fast);

        var dispatch = dispatcher.DispatchAsync(TestData.Raised(TestData.SellAlert()), Ct);

        await Eventually.HoldsAsync(() => fast.Received.Count == 1);
        Assert.False(dispatch.IsCompleted);
        await AdvanceUntilAsync(() => dispatch.IsCompleted);
        await dispatch;
    }

    [Fact]
    public async Task FailingConsumer_DoesNotBlockTheOthers()
    {
        var broken = new FakeConsumer("Slack", _time, failures: int.MaxValue);
        var email = new FakeConsumer("E-mail", _time);
        using var dispatcher = CreateDispatcher(broken, email);

        var dispatch = dispatcher.DispatchAsync(TestData.Raised(TestData.SellAlert()), Ct);

        await Eventually.HoldsAsync(() => email.Received.Count == 1);
        Assert.Single(email.AttemptedAt);
        await AdvanceUntilAsync(() => dispatch.IsCompleted);
        await dispatch;
    }

    [Fact]
    public async Task FailedDelivery_IsRetriedAfter5Then10Then20Seconds()
    {
        var consumer = new FakeConsumer("E-mail", _time, failures: 3);
        using var dispatcher = CreateDispatcher(consumer);

        var dispatch = dispatcher.DispatchAsync(TestData.Raised(TestData.SellAlert()), Ct);
        await AdvanceUntilAsync(() => dispatch.IsCompleted);
        await dispatch;

        var attempts = consumer.AttemptedAt.ToList();
        Assert.Equal([TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20)], attempts.Zip(attempts.Skip(1), (previous, next) => next - previous));
        Assert.Single(consumer.Received);
    }

    [Fact]
    public async Task FailedDelivery_LogsEachRetry()
    {
        var consumer = new FakeConsumer("E-mail", _time, failures: 1);
        using var dispatcher = CreateDispatcher(consumer);

        var dispatch = dispatcher.DispatchAsync(TestData.Raised(TestData.SellAlert()), Ct);
        await AdvanceUntilAsync(() => dispatch.IsCompleted);
        await dispatch;

        var warning = Assert.Single(_log.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.Equal("E-mail failed to handle the Sell alert for PETR4 (attempt 1 of 4), retrying in 5s: E-mail is down", warning.Message);
    }

    [Fact]
    public async Task FailedDelivery_IsGivenUpAfterTheFourthAttempt()
    {
        var consumer = new FakeConsumer("E-mail", _time, failures: int.MaxValue);
        using var dispatcher = CreateDispatcher(consumer);

        var dispatch = dispatcher.DispatchAsync(TestData.Raised(TestData.BuyAlert()), Ct);
        await AdvanceUntilAsync(() => dispatch.IsCompleted);
        await dispatch;

        Assert.Equal(4, consumer.AttemptedAt.Count);
        Assert.Empty(consumer.Received);
        Assert.Equal(TestData.Now.AddSeconds(35), consumer.AttemptedAt.Last());
        Assert.Equal(3, _log.Entries.Count(entry => entry.Level == LogLevel.Warning));
        var error = Assert.Single(_log.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Equal("E-mail gave up on the Buy alert for PETR4 after 4 attempts", error.Message);
        Assert.IsType<InvalidOperationException>(error.Exception);
    }

    [Fact]
    public async Task EmailFailingThenRecovering_IsDeliveredOnRetryAndBothAttemptsAreRecorded()
    {
        using var harness = new MonitoringHarness();
        await harness.CheckPricesAsync(22.70m);
        harness.Smtp.ConnectionFailure = new IOException("Connection refused");

        var dispatch = harness.Dispatcher.DispatchAsync(Assert.Single(harness.Published), Ct);
        await Eventually.HoldsAsync(() => harness.Journal.GetSnapshot().Notifications.Count == 1);
        harness.Smtp.ConnectionFailure = null;
        await harness.AdvanceUntilAsync(() => dispatch.IsCompleted);
        await dispatch;

        Assert.Equal(
            [NotificationStatus.Failed, NotificationStatus.Sent],
            harness.Journal.GetSnapshot().Notifications.Select(notification => notification.Status));
        Assert.StartsWith("[VENDA] PETR4 subiu para R$ 22,70", Assert.Single(harness.Smtp.Delivered).Subject, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmailFailing_TheExampleConsumersReceiveTheAlertExactlyOnce()
    {
        using var harness = new MonitoringHarness();
        var slackLog = new FakeLogger<StockQuoteAlert.Infrastructure.Slack.SlackAlertConsumer>();
        var smsLog = new FakeLogger<StockQuoteAlert.Infrastructure.Sms.SmsAlertConsumer>();
        using var dispatcher = new AlertDispatcher(
            harness.Queue,
            [harness.EmailConsumer, new StockQuoteAlert.Infrastructure.Slack.SlackAlertConsumer(slackLog), new StockQuoteAlert.Infrastructure.Sms.SmsAlertConsumer(smsLog)],
            harness.Time,
            _log);
        await harness.CheckPricesAsync(22.70m);
        harness.Smtp.ConnectionFailure = new IOException("Connection refused");

        var dispatch = dispatcher.DispatchAsync(Assert.Single(harness.Published), Ct);
        await harness.AdvanceUntilAsync(() => dispatch.IsCompleted);
        await dispatch;

        Assert.Single(slackLog.Entries);
        Assert.Single(smsLog.Entries);
        Assert.Equal(AlertDispatcher.MaxAttempts, harness.Smtp.Clients.Count);
        Assert.Equal("E-mail gave up on the Sell alert for PETR4 after 4 attempts", Assert.Single(_log.Entries, entry => entry.Level == LogLevel.Error).Message);
    }

    [Fact]
    public async Task Running_DeliversEveryPublishedAlertInOrder()
    {
        var consumer = new FakeConsumer("E-mail", _time);
        using var dispatcher = CreateDispatcher(consumer);
        var sell = TestData.Raised(TestData.SellAlert());
        var buy = TestData.Raised(TestData.BuyAlert());

        await dispatcher.StartAsync(Ct);
        await _queue.PublishAsync(sell, Ct);
        await _queue.PublishAsync(buy, Ct);
        await Eventually.HoldsAsync(() => consumer.Received.Count == 2);
        await dispatcher.StopAsync(Ct);

        Assert.Equal([sell, buy], consumer.Received);
    }

    [Fact]
    public async Task Running_NextAlertWaitsForTheRetriesOfThePreviousOne()
    {
        var consumer = new FakeConsumer("E-mail", _time, failures: 1);
        using var dispatcher = CreateDispatcher(consumer);
        var sell = TestData.Raised(TestData.SellAlert());
        var buy = TestData.Raised(TestData.BuyAlert());

        await dispatcher.StartAsync(Ct);
        await _queue.PublishAsync(sell, Ct);
        await _queue.PublishAsync(buy, Ct);
        await Eventually.HoldsAsync(() => consumer.AttemptedAt.Count == 1);
        Assert.Empty(consumer.Received);

        await AdvanceUntilAsync(() => consumer.Received.Count == 2);
        await dispatcher.StopAsync(Ct);

        Assert.Equal([sell, buy], consumer.Received);
    }

    [Fact]
    public async Task Stop_WhileWaitingToRetry_CompletesGracefully()
    {
        var consumer = new FakeConsumer("E-mail", _time, failures: int.MaxValue);
        using var dispatcher = CreateDispatcher(consumer);

        await dispatcher.StartAsync(Ct);
        await _queue.PublishAsync(TestData.Raised(TestData.SellAlert()), Ct);
        await Eventually.HoldsAsync(() => consumer.AttemptedAt.Count == 1);
        await dispatcher.StopAsync(Ct);

        Assert.True(dispatcher.ExecuteTask!.IsCompletedSuccessfully);
        Assert.Single(consumer.AttemptedAt);
        Assert.DoesNotContain(_log.Entries, entry => entry.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Stop_WithoutAlerts_CompletesGracefully()
    {
        using var dispatcher = CreateDispatcher(new FakeConsumer("E-mail", _time));

        await dispatcher.StartAsync(Ct);
        await dispatcher.StopAsync(Ct);

        // ExecuteAsync may be cancelled before it even starts; either way the stop must not fault.
        Assert.True(dispatcher.ExecuteTask!.IsCompleted);
        Assert.False(dispatcher.ExecuteTask.IsFaulted);
    }

    [Fact]
    public async Task CancelledDelivery_IsNotRetried()
    {
        using var cancellation = new CancellationTokenSource();
        var consumer = new FakeConsumer("E-mail", _time, cancelWith: cancellation);
        using var dispatcher = CreateDispatcher(consumer);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dispatcher.DispatchAsync(TestData.Raised(TestData.SellAlert()), cancellation.Token));

        Assert.Single(consumer.AttemptedAt);
        Assert.Empty(_log.Entries);
    }

    private AlertDispatcher CreateDispatcher(params IAlertConsumer[] consumers) => new(_queue, consumers, _time, _log);

    private Task AdvanceUntilAsync(Func<bool> condition) =>
        Eventually.HoldsAsync(() =>
        {
            if (condition())
            {
                return true;
            }

            _time.Advance(Step);
            return condition();
        });

    private sealed class FakeConsumer(string name, TimeProvider time, int failures = 0, TimeSpan? holdFor = null, CancellationTokenSource? cancelWith = null) : IAlertConsumer
    {
        private int _remainingFailures = failures;

        public string Name => name;

        public ConcurrentQueue<PriceAlertRaised> Received { get; } = new();

        public ConcurrentQueue<DateTimeOffset> AttemptedAt { get; } = new();

        public async Task ConsumeAsync(PriceAlertRaised alert, CancellationToken cancellationToken)
        {
            AttemptedAt.Enqueue(time.GetUtcNow());

            if (cancelWith is not null)
            {
                await cancelWith.CancelAsync();
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (holdFor is { } hold)
            {
                await Task.Delay(hold, time, cancellationToken);
            }

            if (Interlocked.Decrement(ref _remainingFailures) >= 0)
            {
                throw new InvalidOperationException($"{name} is down");
            }

            Received.Enqueue(alert);
        }
    }
}