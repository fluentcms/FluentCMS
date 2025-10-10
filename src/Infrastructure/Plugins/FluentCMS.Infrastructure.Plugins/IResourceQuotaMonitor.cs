namespace FluentCMS.Infrastructure.Plugins;

/// <summary>
/// Interface for monitoring and enforcing resource quotas for plugins.
/// Implementations should track memory, CPU, and other resource usage per plugin.
/// </summary>
public interface IResourceQuotaMonitor
{
    /// <summary>
    /// Records a metric for a specific plugin.
    /// </summary>
    /// <param name="pluginName">The name of the plugin.</param>
    /// <param name="metricName">The name of the metric (e.g., "MemoryMB", "CpuPercent").</param>
    /// <param name="value">The metric value.</param>
    /// <param name="timestamp">Optional timestamp for the metric. Uses current time if not specified.</param>
    void RecordMetric(string pluginName, string metricName, double value, DateTimeOffset? timestamp = null);

    /// <summary>
    /// Gets all metrics for a specific plugin.
    /// </summary>
    /// <param name="pluginName">The name of the plugin.</param>
    /// <returns>A read-only dictionary of metric names to their current values.</returns>
    IReadOnlyDictionary<string, double> GetCurrentMetrics(string pluginName);

    /// <summary>
    /// Gets historical metrics for a specific plugin and metric.
    /// </summary>
    /// <param name="pluginName">The name of the plugin.</param>
    /// <param name="metricName">The name of the metric.</param>
    /// <param name="timeRange">The time range to retrieve metrics for.</param>
    /// <returns>A list of timestamped metric values.</returns>
    IReadOnlyList<(DateTimeOffset Timestamp, double Value)> GetMetricHistory(string pluginName, string metricName, TimeSpan timeRange);

    /// <summary>
    /// Checks if a plugin is exceeding its resource quotas.
    /// </summary>
    /// <param name="pluginName">The name of the plugin to check.</param>
    /// <returns>A collection of quota violations, if any.</returns>
    IReadOnlyList<ResourceQuotaViolation> CheckQuotas(string pluginName);

    /// <summary>
    /// Gets the current resource usage summary for all plugins.
    /// </summary>
    /// <returns>A summary of resource usage across all plugins.</returns>
    ResourceUsageSummary GetSystemResourceSummary();
}
