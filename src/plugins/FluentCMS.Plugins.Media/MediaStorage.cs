using FluentCMS.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FluentCMS.Plugins.Media;

// The storage of an asset, by the name it recorded: the one in the slot, where new uploads go, or one registered as a keyed
// IFileStorage under that name. The local disk always is, so an asset written there is still read after the slot moves to a
// storage package such as S3.
internal sealed class MediaStorage(IFileStorage current, IServiceProvider services)
{
    public IFileStorage Current => current;

    public IFileStorage? Find(string provider) =>
        provider == current.Name ? current : services.GetKeyedService<IFileStorage>(provider);

    public IFileStorage Of(string provider) =>
        Find(provider)
        ?? throw new InvalidOperationException($"A media asset is in the storage '{provider}', which this instance has neither in the slot, where '{current.Name}' is, nor as a keyed IFileStorage of that name.");
}

// The ready check of the storage new uploads go to, which answers it itself when it is an IHealthCheck, as the local disk and
// the storage packages are. Their answers carry no exception, since the health service would log one whole.
internal sealed class MediaStorageHealthCheck(IFileStorage storage) : IHealthCheck
{
    public const string Name = "media-storage";

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        storage is IHealthCheck check
            ? check.CheckHealthAsync(context, cancellationToken)
            : Task.FromResult(HealthCheckResult.Healthy($"The media storage '{storage.Name}' has no check of its own."));
}
