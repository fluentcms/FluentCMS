using FluentCMS.Activity;
using FluentCMS.Content;
using FluentCMS.Diagnostics;
using FluentCMS.Events;
using FluentCMS.Media;
using FluentCMS.Plugins;
using FluentCMS.Plugins.Media;
using Microsoft.Extensions.DependencyInjection;

[assembly: FluentCmsPlugin(typeof(MediaPlugin))]

namespace FluentCMS.Plugins.Media;

[FluentCmsPlugin("media", SdkRange = "[1.0.0,2.0.0)")]
public sealed class MediaPlugin : IPlugin
{
    private Action<IPluginServiceBuilder>? _storage;

    // The seam of a storage package's UseXxx, such as UseS3: what it adds to the services once the plugin's own are in, and
    // the IFileStorage it puts in the slot for new uploads. The last call wins, and the host's own Providers.Use wins over it.
    public MediaPlugin UseStorage(Action<IPluginServiceBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        _storage = configure;
        return this;
    }

    public void ConfigureServices(IPluginServiceBuilder plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        plugin.Services.AddOptions<MediaOptions>()
            .BindConfiguration(MediaOptions.Section)
            .Validate(
                options => options.IsValid(),
                $"{MediaOptions.Section}: Local:Root must name a directory, MaxRequestBodySize must be positive, SigningKey must be the base64 of at least {MediaSigning.KeyLength} bytes when set, CacheControl a Cache-Control value, and DeletedRetention positive.");
        plugin.Services.AddOptions<DeliveryCacheControl>().BindConfiguration(DeliveryCacheControl.Section);
        plugin.Services.AddScoped<MediaLibraries>();
        plugin.Services.AddScoped<MediaService>();
        plugin.Services.AddScoped<IMediaService>(services => services.GetRequiredService<MediaService>());
        plugin.Services.AddScoped<IMediaResolver, MediaResolver>();
        plugin.Services.AddScoped<IReferenceResolver, MediaReferenceResolver>();
        plugin.Services.AddScoped<MediaTransforms>();
        plugin.Services.AddScoped<MediaOriginals>();
        plugin.Services.AddScoped<MediaCache>();
        plugin.Services.AddSingleton<MediaSigning>();
        plugin.Services.AddSingleton<IInsecureDefaultSource, MediaInsecureDefaults>();
        plugin.Services.AddHostedService<MediaStartup>();
        plugin.Services.AddSingleton<MediaStorage>();
        plugin.Services.AddKeyedSingleton<IFileStorage, LocalFileStorage>(LocalFileStorage.ProviderName);
        plugin.Services.AddHealthChecks()
            .AddCheck<MediaStorageHealthCheck>(MediaStorageHealthCheck.Name, failureStatus: null, [FluentCmsHealthCheckTags.Ready], MediaStorageHealthCheck.Timeout);

        plugin
            .AddFieldType<MediaFieldType>()
            .AddProviderSlot<IFileStorage, LocalFileStorage>()
            .AddProviderSlot<IImageProcessor, SkiaImageProcessor>()
            .AddPermissions<MediaPermissions>()
            .AddSettings<MediaSettings>()
            .AddModel<MediaModel>()
            .AddMigrations(typeof(MediaPlugin).Assembly)
            .AddJob<MediaVariantCleanupJob, MediaVariantCleanupPayload>(MediaVariantCleanupJob.Type)
            .AddErrorCode(MediaErrorCodes.InvalidField, 422, "Invalid media request")
            .AddErrorCode(MediaErrorCodes.TypeNotAllowed, 415, "File type not allowed")
            .AddErrorCode(MediaErrorCodes.TooLarge, 413, "Upload too large")
            .AddErrorCode(MediaErrorCodes.Executable, 415, "Executable file refused")
            .AddErrorCode(MediaErrorCodes.SvgRefused, 415, "SVG refused")
            .AddErrorCode(MediaErrorCodes.AssetNotFound, 404, "Media asset not found")
            .AddErrorCode(MediaErrorCodes.FolderNotFound, 404, "Media folder not found")
            .AddErrorCode(MediaErrorCodes.FolderNameTaken, 409, "Media folder name taken")
            .AddErrorCode(MediaErrorCodes.FolderNotEmpty, 409, "Media folder not empty")
            .AddErrorCode(MediaErrorCodes.FolderCycle, 409, "Media folder moved into itself")
            .AddErrorCode(MediaErrorCodes.SignatureInvalid, 403, "Media URL signature invalid")
            .AddErrorCode(MediaErrorCodes.TransformLimit, 422, "Image transform over a limit")
            .AddErrorCode(MediaErrorCodes.NotTransformable, 415, "Media asset not transformable")
            .AddErrorCode(MediaErrorCodes.PresetNotFound, 404, "Media preset not found")
            .AddErrorCode(MediaErrorCodes.AssetInUse, 409, "Media asset in use")
            .AddErrorCode(MediaErrorCodes.DownloadExpired, 403, "Media download URL expired")
            .AddOutboxHandler<MediaAssetUpdatedEvent, MediaCache>("media.cache.updated")
            .AddOutboxHandler<MediaAssetMovedEvent, MediaCache>("media.cache.moved")
            .AddOutboxHandler<MediaAssetReplacedEvent, MediaCache>("media.cache.replaced")
            .AddOutboxHandler<MediaAssetDeletedEvent, MediaCache>("media.cache.deleted")
            .AddOutboxHandler<SiteDeletedEvent, DeletedSiteMedia>("media.cleanup.site.deleted")
            .AddActivity<MediaAssetUploadedEvent>("media.uploaded", uploaded => Activity("media.uploaded", uploaded, new Dictionary<string, string?> { ["mimeType"] = uploaded.MimeType, ["size"] = uploaded.Size.ToString(System.Globalization.CultureInfo.InvariantCulture) }))
            .AddActivity<MediaAssetUpdatedEvent>("media.updated", updated => Activity("media.updated", updated, null))
            .AddActivity<MediaAssetReplacedEvent>("media.replaced", replaced => Activity("media.replaced", replaced, new Dictionary<string, string?> { ["mimeType"] = replaced.MimeType, ["size"] = replaced.Size.ToString(System.Globalization.CultureInfo.InvariantCulture) }))
            .AddActivity<MediaAssetMovedEvent>("media.moved", moved => Activity("media.moved", moved, new Dictionary<string, string?> { ["from"] = moved.PreviousFolderId?.ToString("D"), ["to"] = moved.FolderId?.ToString("D") }))
            .AddActivity<MediaAssetDeletedEvent>("media.deleted", deleted => Activity("media.deleted", deleted, null));

        _storage?.Invoke(plugin);
    }

    public void Configure(IPluginAppBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapManagement("/media").MapMediaEndpoints();
        app.MapDelivery("/media").MapDeliveryEndpoints();
        app.MapGroup("/media").MapImageEndpoints();
    }

    private static ActivityEntry Activity(string action, MediaAssetEvent asset, IReadOnlyDictionary<string, string?>? details) => new(action)
    {
        TargetType = "media",
        TargetId = asset.AssetId.ToString("D"),
        TargetLabel = asset.FileName,
        Details = details,
    };
}
