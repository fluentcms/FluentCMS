using FluentCMS.Settings;
using FluentCMS.Sites;

namespace FluentCMS.Core.Settings;

// ISettings<T>: the typed face of SettingsService for one settings class.
internal sealed class Settings<T>(SettingsService service, SettingsRegistry registry) : ISettings<T>
    where T : class
{
    private readonly SettingsDescriptor _descriptor = registry.For(typeof(T));

    public async ValueTask<T> GetAsync(SiteId site, CancellationToken cancellationToken = default) =>
        (T)_descriptor.Read((await service.ResolveAsync(_descriptor, site, cancellationToken)).Json);

    public async ValueTask<ResolvedSettings<T>> ResolveAsync(SiteId site, CancellationToken cancellationToken = default)
    {
        var resolved = await service.ResolveAsync(_descriptor, site, cancellationToken);
        return new ResolvedSettings<T>((T)_descriptor.Read(resolved.Json), resolved.InheritedSecrets.Select(_descriptor.PropertyOf));
    }

    public async ValueTask<T> GetDefaultAsync(CancellationToken cancellationToken = default) =>
        (T)_descriptor.Read((await service.ResolveAsync(_descriptor, null, cancellationToken)).Json);

    public async ValueTask SetAsync(SiteId site, T value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        await service.WriteAsync(_descriptor, site, _descriptor.Write(value), null, cancellationToken);
    }

    public async ValueTask SetDefaultAsync(T value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        await service.WriteAsync(_descriptor, null, _descriptor.Write(value), null, cancellationToken);
    }

    public async ValueTask ResetAsync(SiteId site, CancellationToken cancellationToken = default) =>
        await service.ResetAsync(_descriptor, site, null, cancellationToken);

    public async ValueTask ResetDefaultAsync(CancellationToken cancellationToken = default) =>
        await service.ResetAsync(_descriptor, null, null, cancellationToken);
}
