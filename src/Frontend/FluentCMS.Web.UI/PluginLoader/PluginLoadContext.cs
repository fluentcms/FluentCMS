using System.Reflection;
using System.Runtime.Loader;

namespace FluentCMS.Web.UI;

public class PluginLoadContext : AssemblyLoadContext
{
    public PluginLoadContext() : base(isCollectible: true)
    {
    }

    // Override the Load method to handle loading of assemblies
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // We return null here to allow dependencies to be loaded by the default context
        return null;
    }

    // Optionally, you can override Unload to handle any custom cleanup (if needed)
    public new void Unload()
    {
        base.Unload();
        // Add custom cleanup logic if necessary
    }
}
