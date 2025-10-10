namespace FluentCMS.Infrastructure.Plugins;

/// <summary>
/// Contains performance and resource usage metrics for a plugin.
/// This class provides a snapshot of a plugin's resource consumption.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="PluginMetrics"/> class.
/// </remarks>
/// <param name="pluginName">The name of the plugin.</param>
/// <param name="memoryUsageMB">Current memory usage in MB.</param>
/// <param name="cpuUsagePercent">Current CPU usage percentage (0-100).</param>
/// <param name="activeConnections">Number of active connections (database, HTTP, etc.).</param>
/// <param name="requestsPerSecond">Average requests per second handled by the plugin.</param>
/// <param name="lastUpdated">When these metrics were last updated.</param>
public sealed class PluginMetrics(string pluginName, double memoryUsageMB, double cpuUsagePercent, int activeConnections, double requestsPerSecond, DateTimeOffset lastUpdated)
{

    /// <summary>
    /// Gets the name of the plugin.
    /// </summary>
    public string PluginName { get; } = pluginName ?? throw new ArgumentNullException(nameof(pluginName));

    /// <summary>
    /// Gets the current memory usage in MB.
    /// </summary>
    public double MemoryUsageMB { get; } = Math.Max(0, memoryUsageMB);

    /// <summary>
    /// Gets the current CPU usage as a percentage (0-100).
    /// </summary>
    public double CpuUsagePercent { get; } = Math.Clamp(cpuUsagePercent, 0, 100);

    /// <summary>
    /// Gets the number of active connections (database connections, HTTP connections, etc.).
    /// </summary>
    public int ActiveConnections { get; } = Math.Max(0, activeConnections);

    /// <summary>
    /// Gets the average number of requests per second handled by this plugin.
    /// </summary>
    public double RequestsPerSecond { get; } = Math.Max(0, requestsPerSecond);

    /// <summary>
    /// Gets when these metrics were last updated.
    /// </summary>
    public DateTimeOffset LastUpdated { get; } = lastUpdated;

    /// <summary>
    /// Gets a value indicating whether these metrics are considered stale (older than 5 minutes).
    /// </summary>
    public bool IsStale => DateTimeOffset.UtcNow - LastUpdated > TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets a health score for the plugin based on its resource usage (0-100, where 100 is best).
    /// </summary>
    public double HealthScore
    {
        get
        {
            // Simple health score calculation
            // Lower scores for high resource usage
            var memoryScore = Math.Max(0, 100 - MemoryUsageMB);
            var cpuScore = Math.Max(0, 100 - CpuUsagePercent);
            var connectionScore = Math.Max(0, 50 - ActiveConnections);

            return Math.Clamp((memoryScore + cpuScore + connectionScore) / 250.0 * 100.0, 0, 100);
        }
    }

    /// <summary>
    /// Creates a new PluginMetrics instance with updated values.
    /// </summary>
    /// <param name="memoryUsageMB">Updated memory usage in MB.</param>
    /// <param name="cpuUsagePercent">Updated CPU usage percentage.</param>
    /// <param name="activeConnections">Updated active connections count.</param>
    /// <param name="requestsPerSecond">Updated requests per second.</param>
    /// <returns>A new PluginMetrics instance with the updated values.</returns>
    public PluginMetrics WithUpdates(double? memoryUsageMB = null, double? cpuUsagePercent = null, int? activeConnections = null, double? requestsPerSecond = null)
    {
        return new PluginMetrics(
            PluginName,
            memoryUsageMB ?? MemoryUsageMB,
            cpuUsagePercent ?? CpuUsagePercent,
            activeConnections ?? ActiveConnections,
            requestsPerSecond ?? RequestsPerSecond,
            DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Returns a string representation of the plugin metrics.
    /// </summary>
    /// <returns>A formatted string showing key metrics.</returns>
    public override string ToString()
    {
        return $"{PluginName}: {MemoryUsageMB:F1}MB, {CpuUsagePercent:F1}% CPU, {ActiveConnections} connections, {RequestsPerSecond:F1} req/s";
    }
}
