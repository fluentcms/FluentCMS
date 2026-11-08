using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using FluentCMS.Media;
using FluentCMS.Plugins.Media.AzureBlob;
using FluentCMS.Plugins.Media.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Testcontainers.Azurite;
using Testcontainers.Minio;
using Xunit;

namespace FluentCMS.Plugins.Media.Tests;

[CollectionDefinition(Name)]
public sealed class StorageServicesGroup : ICollectionFixture<StorageServices>
{
    public const string Name = "storage services";
}

// MinIO and Azurite in containers, started once for the tests of the storage providers. Where Docker cannot start them, as
// on the Windows runners and on machines without Docker, the tests that need them are skipped and say why.
public sealed class StorageServices : IAsyncLifetime
{
    public const string LocalDisk = "local";
    public const string Minio = "s3";
    public const string Azurite = "azure-blob";
    public const string AzuriteSas = "azure-blob-sas";

    // MinIO publishes no images any more; this is the Pigsty project's continuation of the MinIO community build, pinned by
    // the digest of its RELEASE.2026-08-04T00-00-00Z index.
    private const string MinioImage = "pgsty/minio@sha256:b6bfe7239bfc83fb90d31612d9704d86039dd714f7904b3f1ad68f211e602372";
    private const string AzuriteImage = "mcr.microsoft.com/azure-storage/azurite:3.35.0";

    private static readonly TimeSpan StartTimeout = TimeSpan.FromMinutes(5);

    private MinioContainer? _minio;
    private AzuriteContainer? _azurite;
    private string? _minioMissing;
    private string? _azuriteMissing;

    public async ValueTask InitializeAsync()
    {
        using var timeout = new CancellationTokenSource(StartTimeout);
        var minio = StartAsync(() => new MinioBuilder(MinioImage).Build(), "MinIO", timeout.Token);

        // The Azure SDK asks for a newer service version than an Azurite release knows of.
        var azurite = StartAsync(() => new AzuriteBuilder(AzuriteImage).WithInMemoryPersistence().WithCommand("--skipApiVersionCheck").Build(), "Azurite", timeout.Token);
        (_minio, _minioMissing) = await minio;
        (_azurite, _azuriteMissing) = await azurite;
    }

    public async ValueTask DisposeAsync()
    {
        if (_minio is not null)
        {
            await _minio.DisposeAsync();
        }

        if (_azurite is not null)
        {
            await _azurite.DisposeAsync();
        }
    }

    public void SkipUnlessRunning(string provider)
    {
        var missing = provider switch
        {
            Minio => _minioMissing,
            Azurite or AzuriteSas => _azuriteMissing,
            _ => null,
        };
        Assert.SkipWhen(missing is not null, missing ?? string.Empty);
    }

    // A bucket of its own for a test, so that no test sees another's objects.
    public async Task<S3StorageOptions> BucketAsync(string? keyPrefix = "cms")
    {
        SkipUnlessRunning(Minio);
        var options = new S3StorageOptions
        {
            Endpoint = new Uri(_minio!.GetConnectionString()),
            ForcePathStyle = true,
            Bucket = $"media-{Guid.NewGuid():N}",
            KeyPrefix = keyPrefix,
            AccessKeyId = _minio.GetAccessKey(),
            SecretAccessKey = _minio.GetSecretKey(),
        };
        using var client = options.CreateClient();
        await client.PutBucketAsync(options.Bucket, TestContext.Current.CancellationToken);
        return options;
    }

    public async Task<AzureBlobStorageOptions> ContainerAsync(bool sas = false, string? keyPrefix = "cms")
    {
        SkipUnlessRunning(Azurite);
        var connectionString = _azurite!.GetConnectionString();
        var container = new BlobContainerClient(connectionString, $"media-{Guid.NewGuid():N}");
        await container.CreateAsync(cancellationToken: TestContext.Current.CancellationToken);
        return sas
            ? new AzureBlobStorageOptions
            {
                ContainerUrl = container.GenerateSasUri(BlobContainerSasPermissions.Read | BlobContainerSasPermissions.Write | BlobContainerSasPermissions.Delete | BlobContainerSasPermissions.List, DateTimeOffset.UtcNow.AddHours(1)),
                KeyPrefix = keyPrefix,
            }
            : new AzureBlobStorageOptions { ConnectionString = connectionString, Container = container.Name, KeyPrefix = keyPrefix };
    }

    public BlobServiceClient Account()
    {
        SkipUnlessRunning(Azurite);
        return new BlobServiceClient(_azurite!.GetConnectionString());
    }

    public async Task<IFileStorage> StorageAsync(string provider, DirectoryInfo local) => provider switch
    {
        LocalDisk => LocalStorage(local),
        Minio => new S3FileStorage(Options.Create(await BucketAsync())),
        Azurite => new AzureBlobFileStorage(Options.Create(await ContainerAsync())),
        AzuriteSas => new AzureBlobFileStorage(Options.Create(await ContainerAsync(sas: true))),
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
    };

    public static IFileStorage LocalStorage(DirectoryInfo root) =>
        new LocalFileStorage(Options.Create(new MediaOptions { Local = new LocalStorageOptions { Root = root.FullName } }), new Environment());

    public static IConfiguration Section(S3StorageOptions options) => Section(new Dictionary<string, string?>
    {
        ["Endpoint"] = options.Endpoint?.AbsoluteUri,
        ["Region"] = options.Region,
        ["Bucket"] = options.Bucket,
        ["KeyPrefix"] = options.KeyPrefix,
        ["ForcePathStyle"] = options.ForcePathStyle ? "true" : "false",
        ["AccessKeyId"] = options.AccessKeyId,
        ["SecretAccessKey"] = options.SecretAccessKey,
        ["SessionToken"] = options.SessionToken,
    });

    public static IConfiguration Section(AzureBlobStorageOptions options) => Section(new Dictionary<string, string?>
    {
        ["ConnectionString"] = options.ConnectionString,
        ["Container"] = options.Container,
        ["ContainerUrl"] = options.ContainerUrl?.AbsoluteUri,
        ["KeyPrefix"] = options.KeyPrefix,
    });

    private static IConfiguration Section(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Where(pair => pair.Value is not null)).Build();

    // Building looks for Docker. A Windows host may have none, or one that runs Windows containers only, so any failure skips
    // there; elsewhere only a missing Docker does, and an image that does not start fails the tests.
    private static async Task<(T? Container, string? Missing)> StartAsync<T>(Func<T> build, string name, CancellationToken cancellationToken)
        where T : class, IContainer
    {
        T? container = null;
        try
        {
            container = build();
            await container.StartAsync(cancellationToken);
            return (container, null);
        }
        catch (Exception exception) when (exception is DockerUnavailableException || OperatingSystem.IsWindows())
        {
            if (container is not null)
            {
                await container.DisposeAsync();
            }

            var reason = exception.Message.Split('\n', 2)[0].Trim();
            return (null, $"Docker could not start {name}, so this test needs Docker and is skipped: {exception.GetType().Name}: {reason}");
        }
    }

    private sealed class Environment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
