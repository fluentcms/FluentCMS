using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.AspNetCore.Components.WebAssembly.Services;

namespace FluentCMS.Admin.Plugins;

// Loads the assembly of an admin half, which the app lists as BlazorWebAssemblyLazyLoad, by its name without an extension.
internal interface IAdminAssemblyLoader
{
    Task<Assembly> LoadAsync(string assemblyName, CancellationToken cancellationToken);
}

internal sealed class LazyAdminAssemblyLoader(LazyAssemblyLoader loader) : IAdminAssemblyLoader
{
    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "An admin half is trimmed with the app, which keeps an assembly that does not opt in to trimming whole (spec 2.6).")]
    public async Task<Assembly> LoadAsync(string assemblyName, CancellationToken cancellationToken)
    {
        // The browser's files are named .wasm (webcil), and the loader takes the names the app lists.
        var loaded = await loader.LoadAssembliesAsync([assemblyName + ".wasm"]).WaitAsync(cancellationToken);
        return loaded.FirstOrDefault(assembly => assembly.GetName().Name == assemblyName)
            ?? AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly => assembly.GetName().Name == assemblyName)
            ?? throw new InvalidOperationException($"The assembly {assemblyName} of an admin half was not loaded.");
    }
}
