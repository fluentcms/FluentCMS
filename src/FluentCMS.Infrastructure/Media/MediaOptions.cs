using FluentCMS.Core.Media;
using Microsoft.Extensions.Options;

namespace FluentCMS.Infrastructure.Media;

public sealed class MediaOptions
{
    public const string SectionName = "Media";

    // The storage new uploads go to (MED-04). Assets keep the name of the storage that holds their bytes, so changing it moves new
    // uploads only.
    public string Storage { get; set; } = LocalDiskMediaStorage.StorageName;

    // The largest upload accepted, in bytes.
    public long MaxBytes { get; set; } = 50 * 1024 * 1024;
}

// Its own options, bound to Media:LocalDisk, so the storage does not depend on the choice of storage that is checked against it.
public sealed class LocalDiskOptions
{
    public const string SectionName = "Media:LocalDisk";

    // The host resolves a relative path against its content root.
    public string Root { get; set; } = "media";
}

// An unknown storage or a limit no upload can meet stops the host at startup instead of failing the first upload. The limit stays
// below long.MaxValue, because an upload is read to one byte past it.
internal sealed class MediaOptionsValidation(MediaStorageRegistry storages) : IValidateOptions<MediaOptions>
{
    public ValidateOptionsResult Validate(string? name, MediaOptions options)
    {
        var failures = new List<string>();
        if (storages.Find(options.Storage ?? string.Empty) is null)
        {
            failures.Add($"Media__Storage is '{options.Storage}', which is no registered media storage; the known storages are {string.Join(", ", storages.Names)}.");
        }

        if (options.MaxBytes is <= 0 or long.MaxValue)
        {
            failures.Add($"Media__MaxBytes is {options.MaxBytes}; it must be from 1 to {long.MaxValue - 1} bytes.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
