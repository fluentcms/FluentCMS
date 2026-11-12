using FluentCMS.Core.DependencyInjection;

namespace FluentCMS.Core.Transfer;

// FluentCms:Transfer in configuration.
internal sealed class TransferOptions
{
    internal const string Section = "Transfer";

    // The largest package an import takes as its body, in bytes.
    public long MaxPackageSize { get; set; } = 1L << 30;

    internal void Validate()
    {
        if (MaxPackageSize <= 0)
        {
            throw new FluentCmsConfigurationException(HostErrorCodes.InvalidOption, $"{nameof(MaxPackageSize)} is {MaxPackageSize}; it must be more than 0.");
        }
    }
}
