namespace FluentCMS.Providers;

public class ProviderDiscoveryOptions
{
    /// <summary>
    /// Whether to log the operations
    /// </summary>
    public bool EnableLogging { get; set; } = true;

    /// <summary>
    /// List of assembly name prefixes to scan for IProviderModule and conditions
    /// </summary>
    public List<string> AssemblyPrefixesToScan { get; set; } = [];

    /// <summary>
    /// Whether to ignore exceptions during the process
    /// </summary>
    public bool IgnoreExceptions { get; set; } = false;
}
