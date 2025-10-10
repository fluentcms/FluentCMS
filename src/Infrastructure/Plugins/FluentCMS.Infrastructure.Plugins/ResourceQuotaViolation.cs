namespace FluentCMS.Infrastructure.Plugins;

/// <summary>
/// Represents a resource quota violation.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ResourceQuotaViolation"/> class.
/// </remarks>
/// <param name="metricName">The name of the metric that exceeded its quota.</param>
/// <param name="currentValue">The current value of the metric.</param>
/// <param name="quotaValue">The quota limit that was exceeded.</param>
/// <param name="severity">The severity level of the violation.</param>
public sealed class ResourceQuotaViolation(string metricName, double currentValue, double quotaValue, QuotaViolationSeverity severity)
{

    /// <summary>
    /// Gets the name of the metric that exceeded its quota.
    /// </summary>
    public string MetricName { get; } = metricName ?? throw new ArgumentNullException(nameof(metricName));

    /// <summary>
    /// Gets the current value of the metric.
    /// </summary>
    public double CurrentValue { get; } = currentValue;

    /// <summary>
    /// Gets the quota limit that was exceeded.
    /// </summary>
    public double QuotaValue { get; } = quotaValue;

    /// <summary>
    /// Gets the severity level of the violation.
    /// </summary>
    public QuotaViolationSeverity Severity { get; } = severity;
}
