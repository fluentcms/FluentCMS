using FluentCMS.Core.Media;
using Microsoft.Extensions.Options;

namespace FluentCMS.Infrastructure.Media;

public sealed class MediaOptions
{
    public const string SectionName = "Media";

    // The storage new uploads go to (MED-04). Assets keep the name of the storage that holds their bytes, so changing it moves new
    // uploads only.
    public string Storage { get; set; } = LocalDiskMediaStorage.StorageName;
}

// Its own options, bound to Media:LocalDisk, so the storage does not depend on the choice of storage that is checked against it.
public sealed class LocalDiskOptions
{
    public const string SectionName = "Media:LocalDisk";

    // The host resolves a relative path against its content root.
    public string Root { get; set; } = "media";
}

// An unknown storage name stops the host at startup instead of failing the first upload.
internal sealed class MediaOptionsValidation(MediaStorageRegistry storages) : IValidateOptions<MediaOptions>
{
    public ValidateOptionsResult Validate(string? name, MediaOptions options)
    {
        return storages.Find(options.Storage ?? string.Empty) is not null
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"Media__Storage is '{options.Storage}', which is no registered media storage; the known storages are {string.Join(", ", storages.Names)}.");
    }
}
