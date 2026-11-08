using FluentCMS.Media;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Plugins.Media.S3;

public static class S3MediaExtensions
{
    // Puts a bucket of S3, or of an S3-compatible service, in the media storage slot for new uploads, with the options of the
    // section, checked at start. Assets written before with the local disk stay there and are still served.
    public static MediaPlugin UseS3(this MediaPlugin media, IConfiguration section)
    {
        ArgumentNullException.ThrowIfNull(media);
        ArgumentNullException.ThrowIfNull(section);

        return media.UseStorage(plugin =>
        {
            plugin.Services.AddOptions<S3StorageOptions>()
                .Bind(section)
                .Validate(
                    options => options.IsValid(),
                    "The S3 media storage needs a Bucket named as S3 names buckets; an Endpoint, when set, is an absolute http or https URL without credentials in it; KeyPrefix is segments of letters, digits, dots, hyphens and underscores; and AccessKeyId and SecretAccessKey are given together or not at all, SessionToken only with them.")
                .ValidateOnStart();
            plugin.Providers.Use<IFileStorage, S3FileStorage>();
        });
    }
}
