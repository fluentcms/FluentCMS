using FluentCMS.Core.DependencyInjection;

namespace FluentCMS.Core.Transfer;

// FluentCms:Transfer in configuration. The bounds of an import (spec 4.14): what it takes as its body, what the files of a
// package declare and what it reads of them, so that a small package never costs much more than it holds.
internal sealed class TransferOptions
{
    internal const string Section = "Transfer";

    // The largest package an import takes as its body, in bytes.
    public long MaxPackageSize { get; set; } = 1L << 30;

    // The sum of the sizes the files of a package declare unpacked, and of the files an import opens; MaxPackageSize when
    // not set.
    public long? MaxUnpackedSize { get; set; }

    // The compressed bytes an import reads of its package, a file counting each time it is opened; twice MaxPackageSize
    // when not set.
    public long? MaxReadSize { get; set; }

    public int MaxEntries { get; set; } = 100_000;

    // manifest.json, schema.json, settings.json and roles.json, each.
    public long MaxJsonSize { get; set; } = 64L << 20;

    // The bytes of one line of a .jsonl file.
    public int MaxLineLength { get; set; } = 16 << 20;

    // The conflicts and errors a report lists; every item is counted.
    public int MaxReportItems { get; set; } = 1_000;

    internal long UnpackedLimit => MaxUnpackedSize ?? MaxPackageSize;

    internal long ReadLimit => MaxReadSize ?? (MaxPackageSize > long.MaxValue / 2 ? long.MaxValue : 2 * MaxPackageSize);

    internal void Validate()
    {
        Positive(nameof(MaxPackageSize), MaxPackageSize);
        Positive(nameof(MaxUnpackedSize), MaxUnpackedSize ?? 1);
        Positive(nameof(MaxReadSize), MaxReadSize ?? 1);
        Positive(nameof(MaxEntries), MaxEntries);
        Positive(nameof(MaxJsonSize), MaxJsonSize);
        Positive(nameof(MaxLineLength), MaxLineLength);
        Positive(nameof(MaxReportItems), MaxReportItems);
    }

    private static void Positive(string name, long value)
    {
        if (value <= 0)
        {
            throw new FluentCmsConfigurationException(HostErrorCodes.InvalidOption, $"{name} is {value}; it must be more than 0.");
        }
    }
}
