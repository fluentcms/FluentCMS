using FluentCMS.Media;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Plugins.Media.AzureBlob;

public static class AzureBlobMediaExtensions
{
    // Puts an Azure Blob container in the media storage slot for new uploads, with the options of the section, checked at
    // start. Assets written before with the local disk stay there and are still served.
    public static MediaPlugin UseAzureBlob(this MediaPlugin media, IConfiguration section)
    {
        ArgumentNullException.ThrowIfNull(media);
        ArgumentNullException.ThrowIfNull(section);

        return media.UseStorage(plugin =>
        {
            plugin.Services.AddOptions<AzureBlobStorageOptions>()
                .Bind(section)
                .Validate(
                    options => options.IsValid(),
                    "The Azure Blob media storage needs either a ConnectionString with a Container named as Azure names containers, or a ContainerUrl, an absolute http or https URL with a SAS token in its query, and not both; KeyPrefix is segments of letters, digits, dots, hyphens and underscores.")
                .ValidateOnStart();
            plugin.Providers.Use<IFileStorage, AzureBlobFileStorage>();
        });
    }
}
