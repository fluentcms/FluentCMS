using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Admin.Plugins;

// What would stop the renderer from creating a component of an admin half, checked before it is rendered: an error boundary
// catches what a component throws once it exists, not a failure to create it, which would take the whole admin down.
internal static class PluginComponents
{
    private const string KeptWhole = "An admin half is loaded lazily and kept whole by the app's partial trimming (spec 2.6).";

    // Null when the renderer can create the component with the admin's services; otherwise why it cannot.
    [UnconditionalSuppressMessage("Trimming", "IL2067", Justification = KeptWhole)]
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = KeptWhole)]
    [UnconditionalSuppressMessage("Trimming", "IL2072", Justification = KeptWhole)]
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = KeptWhole)]
    public static string? Problem(Type type, IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(services);
        if (!typeof(IComponent).IsAssignableFrom(type))
        {
            return $"{type.FullName} is not a component.";
        }

        if (!type.IsVisible || type.IsAbstract || type.ContainsGenericParameters)
        {
            return $"{type.FullName} is not a public class that can be created.";
        }

        if (type.GetConstructor(Type.EmptyTypes) is null)
        {
            return $"{type.FullName} has no public parameterless constructor.";
        }

        var isService = services.GetService<IServiceProviderIsService>();
        var isKeyedService = services.GetService<IServiceProviderIsKeyedService>();
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            foreach (var property in current.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (property.GetCustomAttribute<InjectAttribute>() is not { } inject)
                {
                    continue;
                }

                var provided = inject.Key is { } key
                    ? isKeyedService?.IsKeyedService(property.PropertyType, key)
                    : isService?.IsService(property.PropertyType);
                if (provided == false)
                {
                    return $"{type.FullName}.{property.Name} injects {property.PropertyType.FullName}, which the admin's services do not provide. "
                        + "A service the half registers in Configure is reached through IAdminPluginServices.ProviderOf.";
                }
            }
        }

        try
        {
            (Activator.CreateInstance(type) as IDisposable)?.Dispose();
        }
        catch (TargetInvocationException exception)
        {
            return $"The constructor of {type.FullName} throws: {exception.InnerException?.Message}";
        }

        return null;
    }
}
