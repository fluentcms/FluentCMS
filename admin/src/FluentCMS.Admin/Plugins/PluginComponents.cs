using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Admin.Plugins;

// What would stop the renderer from creating a component of an admin half, checked before it is rendered: an error boundary
// catches what a component throws once it exists, not a failure to create it, which would take the whole admin down.
internal static class PluginComponents
{
    private const string ProviderOf = "A service the half registers in Configure is reached through IAdminPluginServices.ProviderOf.";

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

        // The renderer creates a component as ActivatorUtilities does, so its constructor may take services too.
        ObjectFactory factory;
        try
        {
            factory = ActivatorUtilities.CreateFactory(type, Type.EmptyTypes);
        }
        catch (InvalidOperationException exception)
        {
            return $"{type.FullName} cannot be created: {exception.Message}";
        }

        var isService = services.GetService<IServiceProviderIsService>();
        var isKeyedService = services.GetService<IServiceProviderIsKeyedService>();
        var constructors = type.GetConstructors();
        var constructor = constructors.FirstOrDefault(candidate => candidate.IsDefined(typeof(ActivatorUtilitiesConstructorAttribute), false)) ?? constructors.Single();
        foreach (var parameter in constructor.GetParameters())
        {
            var key = parameter.GetCustomAttribute<FromKeyedServicesAttribute>()?.Key;
            var provided = key is not null
                ? isKeyedService?.IsKeyedService(parameter.ParameterType, key)
                : isService?.IsService(parameter.ParameterType);
            if (provided == false && !parameter.HasDefaultValue)
            {
                return $"The constructor of {type.FullName} takes {parameter.Name}, a {parameter.ParameterType.FullName}, which the admin's services do not provide. {ProviderOf}";
            }
        }

        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            foreach (var property in current.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (property.GetCustomAttribute<InjectAttribute>() is not { } inject)
                {
                    continue;
                }

                if (property.SetMethod is null)
                {
                    return $"{type.FullName}.{property.Name} is marked [Inject] and has no setter.";
                }

                var provided = inject.Key is { } injectKey
                    ? isKeyedService?.IsKeyedService(property.PropertyType, injectKey)
                    : isService?.IsService(property.PropertyType);
                if (provided == false)
                {
                    return $"{type.FullName}.{property.Name} injects {property.PropertyType.FullName}, which the admin's services do not provide. {ProviderOf}";
                }
            }
        }

        // Tried once, outside the renderer, and disposed as the renderer would dispose it.
        try
        {
            switch (factory(services, null))
            {
                case IAsyncDisposable asyncDisposable:
                    _ = asyncDisposable.DisposeAsync().AsTask();
                    break;
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }
#pragma warning disable CA1031 // Whatever the constructor throws, the component is reported, not rendered.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            return $"The constructor of {type.FullName} throws: {(exception is TargetInvocationException { InnerException: { } inner } ? inner : exception).Message}";
        }

        return null;
    }
}
