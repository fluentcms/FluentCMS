using System.Collections.Concurrent;

namespace FluentCMS.Infrastructure.Plugins;

/// <summary>
/// Implements IResourceQuotaMonitor for tracking plugin resource usage and quota enforcement.
/// Provides basic in-memory monitoring that can be extended for production use.
/// </summary>
public class ResourceQuotaMonitor : IResourceQuotaMonitor
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, List<(DateTimeOffset Timestamp, double Value)>>> _metrics
        = new();

    // Default quotas - these could be configured
    private readonly Dictionary<string, double> _defaultQuotas = new()
    {
        ["memory_mb"] = 100.0,  // 100 MB per plugin
        ["cpu_percent"] = 10.0,  // 10% CPU per plugin
        ["connections"] = 100.0, // 100 connections per plugin
        ["requests_per_second"] = 100.0 // 100 RPS per plugin
    };

    /// <inheritdoc/>
    public void RecordMetric(string pluginName, string metricName, double value, DateTimeOffset? timestamp = null)
    {
        NullArgumentException.ThrowIfNullOrEmpty(pluginName);
        NullArgumentException.ThrowIfNullOrEmpty(metricName);

        var timestampValue = timestamp ?? DateTimeOffset.Now;

        var pluginMetrics = _metrics.GetOrAdd(pluginName, _ => new ConcurrentDictionary<string, List<(DateTimeOffset, double)>>());
        var metricHistory = pluginMetrics.GetOrAdd(metricName, _ => []);

        // Keep only last 100 measurements per metric
        if (metricHistory.Count >= 100)
        {
            metricHistory.RemoveAt(0);
        }

        metricHistory.Add((timestampValue, value));
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, double> GetCurrentMetrics(string pluginName)
    {
        NullArgumentException.ThrowIfNullOrEmpty(pluginName);

        var result = new Dictionary<string, double>();

        if (_metrics.TryGetValue(pluginName, out var pluginMetrics))
        {
            foreach (var (metricName, history) in pluginMetrics)
            {
                if (history.Count > 0)
                {
                    // Return the most recent value
                    result[metricName] = history[history.Count - 1].Value;
                }
            }
        }

        return result;
    }

    /// <inheritdoc/>
    public IReadOnlyList<(DateTimeOffset Timestamp, double Value)> GetMetricHistory(
        string pluginName,
        string metricName,
        TimeSpan timeRange)
    {
        NullArgumentException.ThrowIfNullOrEmpty(pluginName);
        NullArgumentException.ThrowIfNullOrEmpty(metricName);

        if (!_metrics.TryGetValue(pluginName, out var pluginMetrics) ||
            !pluginMetrics.TryGetValue(metricName, out var history))
        {
            return [];
        }

        var cutoff = DateTimeOffset.Now.Subtract(timeRange);
        return history
            .Where(point => point.Timestamp >= cutoff)
            .ToList()
            .AsReadOnly();
    }

    /// <inheritdoc/>
    public IReadOnlyList<ResourceQuotaViolation> CheckQuotas(string pluginName)
    {
        NullArgumentException.ThrowIfNullOrEmpty(pluginName);

        var violations = new List<ResourceQuotaViolation>();
        var currentMetrics = GetCurrentMetrics(pluginName);

        foreach (var (metricName, currentValue) in currentMetrics)
        {
            if (_defaultQuotas.TryGetValue(metricName, out var quota))
            {
                if (currentValue > quota)
                {
                    var severity = DetermineViolationSeverity(currentValue, quota);
                    violations.Add(new ResourceQuotaViolation(metricName, currentValue, quota, severity));
                }
            }
        }

        return violations.AsReadOnly();
    }

    /// <inheritdoc/>
    public ResourceUsageSummary GetSystemResourceSummary()
    {
        var totalMemoryMB = 0.0;
        var totalCpuPercent = 0.0;
        var activePluginsCount = _metrics.Count;
        var totalViolations = 0;

        foreach (var pluginMetrics in _metrics.Values)
        {
            foreach (var (metricName, history) in pluginMetrics)
            {
                if (history.Count > 0)
                {
                    var latestValue = history[history.Count - 1].Value;

                    switch (metricName.ToLowerInvariant())
                    {
                        case "memory_mb":
                            totalMemoryMB += latestValue;
                            break;
                        case "cpu_percent":
                            totalCpuPercent += latestValue;
                            break;
                    }
                }
            }

            // Check for violations in this plugin
            var violations = CheckQuotas(pluginMetrics.Keys.FirstOrDefault() ?? "");
            totalViolations += violations.Count(v => v.Severity >= QuotaViolationSeverity.Warning);
        }

        return new ResourceUsageSummary(totalMemoryMB, totalCpuPercent, activePluginsCount, totalViolations);
    }

    /// <summary>
    /// Determines the severity of a quota violation based on how much it exceeds the limit.
    /// </summary>
    /// <param name="currentValue">The current value.</param>
    /// <param name="quota">The quota limit.</param>
    /// <returns>The violation severity level.</returns>
    private static QuotaViolationSeverity DetermineViolationSeverity(double currentValue, double quota)
    {
        var excessPercent = (currentValue - quota) / quota;

        return excessPercent switch
        {
            < 0.1 => QuotaViolationSeverity.Warning,      // < 10% over
            < 0.5 => QuotaViolationSeverity.Moderate,     // < 50% over
            < 1.0 => QuotaViolationSeverity.Severe,       // < 100% over
            _ => QuotaViolationSeverity.Critical           // > 100% over
        };
    }

    /// <summary>
    /// Sets a quota for a specific metric type.
    /// </summary>
    /// <param name="metricType">The metric type to set quota for.</param>
    /// <param name="quota">The quota value.</param>
    public void SetQuota(string metricType, double quota)
    {
        NullArgumentException.ThrowIfNullOrEmpty(metricType);

        _defaultQuotas[metricType] = quota;
    }

    /// <summary>
    /// Clears all metrics for a specific plugin.
    /// </summary>
    /// <param name="pluginName">The plugin name.</param>
    public void ClearMetrics(string pluginName)
    {
        NullArgumentException.ThrowIfNullOrEmpty(pluginName);

        _metrics.TryRemove(pluginName, out _);
    }

    /// <summary>
    /// Clears all metrics.
    /// </summary>
    public void ClearAllMetrics() =>
        _metrics.Clear();
}
