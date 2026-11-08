using FluentCMS.Activity;
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
    public void ConfigureServices(IPluginServiceBuilder plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);

        plugin.Services.AddOptions<MediaOptions>()
            .BindConfiguration(MediaOptions.Section)
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Local?.Root) && options.MaxRequestBodySize > 0,
                $"{MediaOptions.Section}: Local:Root must name a directory, and MaxRequestBodySize must be positive.");
        plugin.Services.AddScoped<IMediaService, MediaService>();
        plugin.Services.AddSingleton<IInsecureDefaultSource, MediaInsecureDefaults>();

        plugin
            .AddProviderSlot<IFileStorage, LocalFileStorage>()
            .AddPermissions<MediaPermissions>()
            .AddSettings<MediaSettings>()
            .AddModel<MediaModel>()
            .AddMigrations(typeof(MediaPlugin).Assembly)
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
            .AddActivity<MediaAssetUploadedEvent>("media.uploaded", uploaded => Activity("media.uploaded", uploaded, new Dictionary<string, string?> { ["mimeType"] = uploaded.MimeType, ["size"] = uploaded.Size.ToString(System.Globalization.CultureInfo.InvariantCulture) }))
            .AddActivity<MediaAssetUpdatedEvent>("media.updated", updated => Activity("media.updated", updated, null))
            .AddActivity<MediaAssetMovedEvent>("media.moved", moved => Activity("media.moved", moved, new Dictionary<string, string?> { ["from"] = moved.PreviousFolderId?.ToString("D"), ["to"] = moved.FolderId?.ToString("D") }))
            .AddActivity<MediaAssetDeletedEvent>("media.deleted", deleted => Activity("media.deleted", deleted, null));
    }

    public void Configure(IPluginAppBuilder app) => app.MapManagement("/media").MapMediaEndpoints();

    private static ActivityEntry Activity(string action, MediaAssetEvent asset, IReadOnlyDictionary<string, string?>? details) => new(action)
    {
        TargetType = "media",
        TargetId = asset.AssetId.ToString("D"),
        TargetLabel = asset.FileName,
        Details = details,
    };
}
