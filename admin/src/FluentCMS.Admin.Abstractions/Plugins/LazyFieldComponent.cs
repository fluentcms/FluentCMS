using System.Diagnostics.CodeAnalysis;

namespace FluentCMS.Admin.Plugins;

// A field editor or list cell of an admin half that is loaded on first use. The component is answered only while the half
// may be used, so a half loaded in one site gives nothing in a site where its plugin is disabled.
internal sealed class LazyFieldComponent(Func<bool> isAvailable, Func<CancellationToken, Task<Type>> load, Type baseType)
{
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    private Type? _loaded;

    public bool Available => isAvailable();

    public bool Loaded([NotNullWhen(true)][DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] out Type? component)
    {
        component = isAvailable() ? _loaded : null;
        return component is not null;
    }

    // Whether the half may be used, loaded first when it is not.
    public async Task<bool> LoadAsync(CancellationToken cancellationToken)
    {
        if (!isAvailable())
        {
            return false;
        }

        if (_loaded is null)
        {
            Keep(await load(cancellationToken));
        }

        return true;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2069", Justification = "The component is a type of an admin half, which the app's partial trimming keeps whole (spec 2.6).")]
    private void Keep(Type component)
    {
        if (!baseType.IsAssignableFrom(component))
        {
            throw new InvalidOperationException($"{component.FullName} does not derive from {baseType.Name}.");
        }

        _loaded = component;
    }
}
