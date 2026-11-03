using System.Collections;

namespace StockQuoteAlert.Core.Application.Monitoring;

/// <summary>The stocks being monitored, in the order they were given. Never empty and never repeats a ticker.</summary>
public sealed class MonitoringTargets : IReadOnlyList<MonitoringTarget>
{
    private readonly MonitoringTarget[] _targets;

    public MonitoringTargets(IEnumerable<MonitoringTarget> targets)
    {
        ArgumentNullException.ThrowIfNull(targets);

        _targets = [.. targets];

        if (_targets.Length == 0)
        {
            throw new ArgumentException("At least one stock must be monitored.", nameof(targets));
        }

        if (_targets.Any(target => target is null))
        {
            throw new ArgumentException("A monitored stock cannot be null.", nameof(targets));
        }

        if (_targets.GroupBy(target => target.Ticker).FirstOrDefault(group => group.Count() > 1) is { } duplicate)
        {
            throw new ArgumentException($"{duplicate.Key} is monitored more than once.", nameof(targets));
        }
    }

    public int Count => _targets.Length;

    public MonitoringTarget this[int index] => _targets[index];

    public IEnumerator<MonitoringTarget> GetEnumerator() => ((IEnumerable<MonitoringTarget>)_targets).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}