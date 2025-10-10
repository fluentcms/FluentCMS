namespace FluentCMS.Infrastructure.Plugins;

/// <summary>
/// Summary of resource usage across all plugins.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="ResourceUsageSummary"/> class.
/// </remarks>
/// <param name="totalMemoryMB">Total memory usage in MB across all plugins.</param>
/// <param name="totalCpuPercent">Total CPU usage percentage across all plugins.</param>
/// <param name="activePlugins">Number of active plugins.</param>
/// <param name="violations">Number of current quota violations.</param>
public sealed class ResourceUsageSummary(double totalMemoryMB, double totalCpuPercent, int activePlugins, int violations)
{

    /// <summary>
    /// Gets the total memory usage in MB across all plugins.
    /// </summary>
    public double TotalMemoryMB { get; } = totalMemoryMB;

    /// <summary>
    /// Gets the total CPU usage percentage across all plugins.
    /// </summary>
    public double TotalCpuPercent { get; } = totalCpuPercent;

    /// <summary>
    /// Gets the number of active plugins.
    /// </summary>
    public int ActivePlugins { get; } = activePlugins;

    /// <summary>
    /// Gets the number of current quota violations.
    /// </summary>
    public int Violations { get; } = violations;
}
