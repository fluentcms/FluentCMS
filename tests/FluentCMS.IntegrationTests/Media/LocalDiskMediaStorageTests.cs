using FluentCMS.Core.Media;
using FluentCMS.Infrastructure;
using FluentCMS.Infrastructure.Media;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FluentCMS.IntegrationTests.Media;

public sealed class LocalDiskMediaStorageTests : MediaStorageConformanceTests
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"fluentcms-media-{Guid.NewGuid():N}");

    protected override string StorageName => "local";

    [Fact]
    public async Task Bytes_land_under_the_root_and_nothing_outside_it_is_touched()
    {
        Directory.CreateDirectory(_root);
        var outside = Path.Combine(Path.GetDirectoryName(_root)!, $"secret-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(outside, "keep me");

        try
        {
            var stored = await SaveAsync([7, 8, 9]);
            var written = Assert.Single(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));

            Assert.Equal(Path.Combine(_root, stored.Key[..2], stored.Key[2..4], stored.Key), written);
            Assert.False(await Storage.DeleteAsync($"../{Path.GetFileName(outside)}"));
            Assert.Equal("keep me", await File.ReadAllTextAsync(outside));
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public void The_configured_storage_must_be_registered()
    {
        using var services = TestServices.Build(new Dictionary<string, string?>
        {
            ["Database:ConnectionString"] = "Data Source=unused.db",
            ["Media:Storage"] = "s3",
        });

        var exception = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<MediaOptions>>().Value);

        Assert.Contains("'s3', which is no registered media storage; the known storages are local", exception.Message);
        Assert.Equal("local", services.GetRequiredService<MediaStorageRegistry>().Names.Single());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("9223372036854775807")]
    public void The_upload_limit_must_be_one_an_upload_can_meet(string maxBytes)
    {
        using var services = TestServices.Build(new Dictionary<string, string?>
        {
            ["Database:ConnectionString"] = "Data Source=unused.db",
            ["Media:MaxBytes"] = maxBytes,
        });

        var exception = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<MediaOptions>>().Value);

        Assert.Contains($"Media__MaxBytes is {maxBytes}", exception.Message);
    }

    [Fact]
    public async Task An_unknown_storage_stops_the_host_from_starting()
    {
        using var install = new TemporaryInstall();

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(
            () => AdminHost.StartAsync(install, settings: new Dictionary<string, string?> { ["Media:Storage"] = "s3" }));

        Assert.Contains("Media__Storage is 's3'", exception.Message);
    }

    protected override void AddStorage(IServiceCollection services)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = "Data Source=unused.db",
                ["Media:LocalDisk:Root"] = _root,
            })
            .Build();
        services.AddFluentCmsInfrastructure(configuration);
    }

    protected override void Cleanup()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
