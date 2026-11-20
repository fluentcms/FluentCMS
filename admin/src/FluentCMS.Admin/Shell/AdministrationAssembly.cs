using System.Reflection;
using FluentCMS.Admin.Plugins;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Admin.Shell;

// The assembly of core's administration pages, loaded once per page load, on the first visit of a route of one of its areas
// that the user may open. A load that fails is kept, so that the route shows it with a retry, until the next navigation
// tries again.
internal sealed class AdministrationAssembly(IAdminAssemblyLoader loader, ILogger<AdministrationAssembly> logger)
{
    private Task<Assembly>? _loading;

    public Assembly? Loaded { get; private set; }

    public bool Failed { get; private set; }

    public void TryAgain() => Failed = false;

    // One load: the callers of a load in progress wait for the same one.
    public async Task<Assembly?> TryLoadAsync(CancellationToken cancellationToken)
    {
        if (Loaded is not null || Failed)
        {
            return Loaded;
        }

        try
        {
            return await (_loading ??= LoadAsync()).WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
#pragma warning disable CA1031 // Whatever stops the assembly from loading, the admin goes on without its pages.
        catch (Exception)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    private async Task<Assembly> LoadAsync()
    {
        try
        {
            return Loaded = await loader.LoadAsync(AdministrationAreas.AssemblyName, CancellationToken.None);
        }
        catch (Exception exception)
        {
            ShellLog.AdministrationNotLoaded(logger, exception);
            Failed = true;
            throw;
        }
        finally
        {
            _loading = null;
        }
    }
}
