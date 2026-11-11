using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentCMS.Authorization;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Storage;
using FluentCMS.Media;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;
using Role = FluentCMS.Core.Storage.Entities.Role;
using RoleGrant = FluentCMS.Core.Storage.Entities.RoleGrant;
using static FluentCMS.Plugins.Media.Tests.MediaHost;
using static FluentCMS.Plugins.Media.Tests.Transforms;

namespace FluentCMS.Plugins.Media.Tests;

// IMediaService.OpenImageAsync: the pixels of an image for code in the host, read as the caller.
public sealed class MediaOpenImageTests(TransformHosts hosts) : IClassFixture<TransformHosts>
{
    [Fact]
    public async Task ACallerWhoMayReadAnAssetOpensACopyInTheSizeAndTypeAskedWithoutItsMetadata()
    {
        var media = hosts.Media;
        using var admin = await media.AdminAsync();
        var asset = await UploadAsync(admin, TurnedJpeg(400, 200), "turned.jpg");
        using var hidden = await admin.PatchAsJsonAsync(new Uri($"{Root}/{asset.Id:D}", UriKind.Relative), new { isPrivate = true }, Token);
        hidden.StatusCode.ShouldBe(HttpStatusCode.OK);
        var reader = await ActorAsync(media, "media.read");

        var (type, bytes) = await OpenAsync(reader, asset.Id, new MediaImageOptions { Width = 100, Height = 100, Format = MediaImageFormat.Png });
        var (ownType, own) = await OpenAsync(reader, asset.Id, new MediaImageOptions());

        // A private asset is read too, shown as its orientation says, and the copy carries no EXIF.
        type.ShouldBe("image/png");
        using (var image = Decode(bytes))
        {
            (image.Width, image.Height).ShouldBe((50, 100));
        }

        ownType.ShouldBe("image/jpeg");
        using (var image = Decode(own))
        {
            (image.Width, image.Height).ShouldBe((200, 400));
        }

        Encoding.Latin1.GetString(own).ShouldNotContain("Exif");
    }

    [Fact]
    public async Task ACallerWhoMayNotReadTheAssetAndAnAssetThatIsNoImageAreRefused()
    {
        var media = hosts.Media;
        using var admin = await media.AdminAsync();
        var asset = await UploadAsync(admin, Bands(40, 20));
        var document = await UploadAsync(admin, Files.Pdf(), "notes.pdf");
        var reader = await ActorAsync(media, "media.read");
        var stranger = await ActorAsync(media, "content.post.read");
        var options = new MediaImageOptions { Width = 10 };

        await Should.ThrowAsync<PermissionDeniedException>(() => OpenAsync(stranger, asset.Id, options));
        (await Should.ThrowAsync<PluginException>(() => OpenAsync(reader, Guid.CreateVersion7(), options))).Code.ShouldBe(MediaErrorCodes.AssetNotFound);
        (await Should.ThrowAsync<PluginException>(() => OpenAsync(reader, document.Id, options))).Code.ShouldBe(MediaErrorCodes.NotTransformable);
        (await Should.ThrowAsync<PluginException>(() => OpenAsync(reader, asset.Id, new MediaImageOptions { Width = 100_000 }))).Code.ShouldBe(MediaErrorCodes.TransformLimit);
        (await Should.ThrowAsync<ValidationException>(() => OpenAsync(reader, asset.Id, new MediaImageOptions { Preset = "thumb" }))).Errors.ShouldHaveSingleItem().Code.ShouldBe(MediaErrorCodes.InvalidField);
    }

    // Under the shared library the image is a row of the default site, and media.read is asked in the site of the caller,
    // with the folders its grant names there.
    [Fact]
    public async Task UnderTheSharedLibraryAnotherSiteOpensTheImageWithItsOwnPermissionAndFolderLimits()
    {
        var media = hosts.Media;
        using var admin = await media.AdminAsync();
        var site = await MediaLibraryTesting.CreateSiteAsync(media);
        var open = await FolderAsync(admin, Unique("open"));
        var closed = await FolderAsync(admin, Unique("closed"));
        var reachable = Id((await UploadedAsync(admin, open, ("a.png", Bands(40, 20)))).Single()!);
        var hidden = Id((await UploadedAsync(admin, closed, ("b.png", Bands(40, 20)))).Single()!);
        var role = new Role { Id = Guid.CreateVersion7(), Name = Unique("reader") };
        await using (var scope = media.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
            context.Set<Role>().Add(role);
            context.Set<RoleGrant>().Add(new RoleGrant
            {
                Id = Guid.CreateVersion7(),
                RoleId = role.Id,
                Permission = "media.read",
                Constraints = new PermissionConstraints { Sites = [new SiteId(site)], Resources = [open.ToString()] }.ToJson(),
            });
            await context.SaveChangesAsync(Token);
        }

        var reader = media.Host.As(await media.Host.CreateApiKeyAsync(ApiKeyClass.Management, role.Name, cancellationToken: Token));
        var options = new MediaImageOptions { Width = 10 };

        var (type, bytes) = await OpenAsync(reader.In(new SiteId(site)), reachable, options);

        type.ShouldBe("image/png");
        using (var image = Decode(bytes))
        {
            (image.Width, image.Height).ShouldBe((10, 5));
        }

        (await Should.ThrowAsync<PluginException>(() => OpenAsync(reader.In(new SiteId(site)), hidden, options))).Code.ShouldBe(MediaErrorCodes.AssetNotFound);
        await Should.ThrowAsync<PermissionDeniedException>(() => OpenAsync(reader, reachable, options));
    }

    // A caller in this process whose role holds the permission alone.
    private static async Task<TestActor> ActorAsync(MediaHost media, string permission)
    {
        var role = $"role-{Guid.NewGuid():N}";
        await media.Host.CreateRoleAsync(role, [permission], Token);
        return media.Host.As(await media.Host.CreateApiKeyAsync(ApiKeyClass.Management, role, cancellationToken: Token));
    }

    private static Task<(string Type, byte[] Bytes)> OpenAsync(TestActor actor, Guid id, MediaImageOptions options) =>
        actor.RunAsync<IMediaService, (string, byte[])>(async service =>
        {
            var copy = await service.OpenImageAsync(id, options, Token);
            await using (copy.Content)
            {
                using var bytes = new MemoryStream();
                await copy.Content.CopyToAsync(bytes, Token);
                return (copy.MimeType, bytes.ToArray());
            }
        });
}
