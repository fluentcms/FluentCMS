using System.Reflection;
using System.Runtime.Loader;

namespace FluentCMS.Web.UI;

public class PluginLoadContext : AssemblyLoadContext
{
    public PluginLoadContext() : base(isCollectible: true)
    {
    }
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        return null; // Return null to use the default load context for dependencies
    }
}
