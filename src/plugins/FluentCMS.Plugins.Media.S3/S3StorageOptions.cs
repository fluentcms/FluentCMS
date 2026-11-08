using System.Text.RegularExpressions;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;

namespace FluentCMS.Plugins.Media.S3;

// Bound from the section given to UseS3. The access key is both of its fields or neither; without it, the AWS SDK finds the
// credentials itself, from the environment, a profile or the role of the machine. No value of it is ever logged or reported.
public sealed partial class S3StorageOptions
{
    public const string DefaultSigningRegion = "us-east-1";

    // An S3-compatible service, such as http://minio:9000; left out, AWS in the region.
    public Uri? Endpoint { get; set; }

    // The AWS region, such as eu-west-1; left out, the SDK's own, from AWS_REGION or a profile. With an endpoint, the region
    // requests are signed for, us-east-1 by default.
    public string? Region { get; set; }

    public string Bucket { get; set; } = string.Empty;

    // Put before every key, such as media/; segments of a key, joined by slashes.
    public string? KeyPrefix { get; set; }

    // bucket.host/key or host/bucket/key; most S3-compatible services need the second.
    public bool ForcePathStyle { get; set; }

    public string? AccessKeyId { get; set; }

    public string? SecretAccessKey { get; set; }

    public string? SessionToken { get; set; }

    internal string Prefix => KeyPrefix?.Trim('/') is { Length: > 0 } prefix ? prefix + "/" : string.Empty;

    internal bool IsValid() =>
        BucketName().IsMatch(Bucket ?? string.Empty)
        && (Endpoint is null || (Endpoint.IsAbsoluteUri && Endpoint.Scheme is "http" or "https" && string.IsNullOrEmpty(Endpoint.UserInfo)))
        && (Region is null || Region.Length > 0)
        && (string.IsNullOrEmpty(KeyPrefix?.Trim('/')) || FileStorageKeys.IsValid(KeyPrefix.Trim('/')))
        && string.IsNullOrEmpty(AccessKeyId) == string.IsNullOrEmpty(SecretAccessKey)
        && (string.IsNullOrEmpty(SessionToken) || !string.IsNullOrEmpty(AccessKeyId));

    internal AmazonS3Client CreateClient()
    {
        var config = new AmazonS3Config { ForcePathStyle = ForcePathStyle };
        if (Endpoint is not null)
        {
            config.ServiceURL = Endpoint.AbsoluteUri;
            config.AuthenticationRegion = Region ?? DefaultSigningRegion;

            // The checksum trailers the SDK sends by default are unknown to many S3-compatible services.
            config.RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED;
            config.ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED;
        }
        else if (Region is not null)
        {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(Region);
        }

        if (string.IsNullOrEmpty(AccessKeyId))
        {
            return new AmazonS3Client(config);
        }

        AWSCredentials credentials = string.IsNullOrEmpty(SessionToken)
            ? new BasicAWSCredentials(AccessKeyId, SecretAccessKey)
            : new SessionAWSCredentials(AccessKeyId, SecretAccessKey, SessionToken);
        return new AmazonS3Client(credentials, config);
    }

    [GeneratedRegex(@"^[a-z0-9][a-z0-9.-]{1,61}[a-z0-9]\z", RegexOptions.CultureInvariant)]
    private static partial Regex BucketName();
}
