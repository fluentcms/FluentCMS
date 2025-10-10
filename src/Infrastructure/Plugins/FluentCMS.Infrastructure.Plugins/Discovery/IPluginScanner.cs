namespace FluentCMS.Infrastructure.Plugins.Discovery;

/// <summary>
/// Defines the contract for scanning assemblies and discovering plugin implementations.
/// This interface uses the Strategy pattern to allow different scanning implementations.
/// </summary>
public interface IPluginScanner
{
    IEnumerable<string> GetPluginTypes(CancellationToken cancellationToken = default);
}
