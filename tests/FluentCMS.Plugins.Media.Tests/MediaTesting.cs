using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Core.Authorization;
using FluentCMS.Core.Events;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Storage;
using FluentCMS.Core.Storage.Entities;
using FluentCMS.Settings;
using FluentCMS.Sites;
using FluentCMS.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.Media.Tests;

// A host whose local storage is a directory of its own, removed with it, and whose job runner has no workers, so that a
// test runs the outbox itself.
internal sealed class MediaHost : IAsyncDisposable
{
    public const string Root = "/api/management/v1/media";

    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("fluentcms-media-");

    public MediaHost()
    {
        Host = new FluentCmsTestHost()
            .WithServices(services => services.PostConfigure<MediaOptions>(options => options.Local.Root = _directory.FullName))
            .Configure(cms =>
            {
                cms.Jobs.Options.Queues[JobRunnerOptions.DefaultQueue] = 0;
                cms.Jobs.Options.Queues[OutboxDrainJob.Queue] = 0;
            });
    }

    public static CancellationToken Token => TestContext.Current.CancellationToken;

    public FluentCmsTestHost Host { get; }

    public string StorageRoot => _directory.FullName;

    public IServiceProvider Services => Host.Services;

    public async Task<HttpClient> AdminAsync() =>
        Host.AsKey(await Host.CreateApiKeyAsync(ApiKeyClass.Management, BuiltInRoles.SuperAdminName, cancellationToken: Token));

    // A key of a new role with the grants given, written through role management as an administrator would.
    public async Task<HttpClient> ClientWithAsync(HttpClient admin, params object[] grants)
    {
        var name = "role-" + Guid.NewGuid().ToString("N");
        using var role = await admin.PostAsJsonAsync(new Uri("/api/management/v1/roles", UriKind.Relative), new { name, grants }, Token);
        role.StatusCode.ShouldBe(HttpStatusCode.Created, await role.Content.ReadAsStringAsync(Token));
        return Host.AsKey(await Host.CreateApiKeyAsync(ApiKeyClass.Management, name, cancellationToken: Token));
    }

    public async Task ConfigureAsync(Action<MediaSettings> change)
    {
        await using var scope = Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        using (SystemOperation.Begin(services.GetRequiredService<IOperationContextAccessor>(), Origin.Seed, new SiteId(DefaultSite.Id)))
        {
            var media = services.GetRequiredService<ISettings<MediaSettings>>();
            var current = await media.GetAsync(new SiteId(DefaultSite.Id), Token);
            change(current);
            await media.SetAsync(new SiteId(DefaultSite.Id), current, Token);
        }
    }

    public async Task AddLocaleAsync(string code)
    {
        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SiteManager>().EnsureLocaleAsync(DefaultSite.Id, code, Token);
    }

    public static async Task<Guid> EntryAsync(HttpClient admin, string type, string title)
    {
        using var response = await admin.PostAsJsonAsync(new Uri($"/api/management/v1/content/{type}", UriKind.Relative), new { title }, Token);
        return Id(await JsonAsync(response, HttpStatusCode.Created));
    }

    // A reference written straight into cms_reference, as the media field type will write it (#2835).
    public async Task ReferAsync(string type, Guid entry, string field, Guid asset, short state = ContentState.Draft, string locale = "")
    {
        await using var scope = Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<CmsDbContext>();
        database.References.Add(new Reference { Id = Guid.CreateVersion7(), SourceType = type, SourceDocumentId = entry, State = state, Locale = locale, Field = field, TargetType = MediaService.ReferenceTarget, TargetId = asset });
        await database.SaveChangesAsync(Token);
    }

    public async Task<int> ReferencesToAsync(Guid asset)
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<CmsDbContext>().References.CountAsync(reference => reference.TargetType == MediaService.ReferenceTarget && reference.TargetId == asset, Token);
    }

    public async Task DrainOutboxAsync()
    {
        while (await Services.GetRequiredService<OutboxDrainer>().DrainAsync(Token) > 0)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Host.DisposeAsync();
        _directory.Delete(recursive: true);
    }

    public static async Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid? folderId, params (string Name, byte[] Bytes)[] files)
    {
        using var form = new MultipartFormDataContent();
        foreach (var (name, bytes) in files)
        {
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            form.Add(content, "files", name);
        }

        if (folderId is { } folder)
        {
            form.Add(new StringContent(folder.ToString("D")), "folderId");
        }

        return await client.PostAsync(new Uri(Root, UriKind.Relative), form, Token);
    }

    public static async Task<JsonArray> UploadedAsync(HttpClient client, Guid? folderId, params (string Name, byte[] Bytes)[] files)
    {
        using var response = await UploadAsync(client, folderId, files);
        var text = await response.Content.ReadAsStringAsync(Token);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, text);
        return JsonNode.Parse(text)!["items"]!.AsArray();
    }

    public static async Task<Guid> FolderAsync(HttpClient client, string name, Guid? parentId = null)
    {
        using var response = await client.PostAsJsonAsync(new Uri($"{Root}/folders", UriKind.Relative), new { name, parentId }, Token);
        var text = await response.Content.ReadAsStringAsync(Token);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, text);
        return Id(JsonNode.Parse(text)!);
    }

    public static async Task<JsonNode> JsonAsync(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        var text = await response.Content.ReadAsStringAsync(Token);
        response.StatusCode.ShouldBe(status, text);
        return JsonNode.Parse(text)!;
    }

    public static async Task<JsonNode> GetAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), Token);
        return await JsonAsync(response);
    }

    public static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync(Token))?["code"]?.GetValue<string>();

    public static Guid Id(JsonNode node, string name = "id") => Guid.Parse(node[name]!.GetValue<string>());
}

// Files that carry the headers of their format, enough for detection and for the image size, and nothing else.
internal static class Files
{
    public static byte[] Jpeg(int width, int height)
    {
        byte[] app0 = [0xFF, 0xE0, 0x00, 0x10, .. "JFIF\0"u8, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00];
        byte[] sof = [0xFF, 0xC0, 0x00, 0x11, 0x08, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 0x03, 0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01];
        return [0xFF, 0xD8, .. app0, .. sof, 0xFF, 0xD9];
    }

    public static byte[] Png(int width, int height)
    {
        var ihdr = new byte[25];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, 13);
        "IHDR"u8.CopyTo(ihdr.AsSpan(4));
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(8), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(12), (uint)height);
        ihdr[16] = 8;
        ihdr[17] = 6;
        return [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. ihdr, 0, 0, 0, 0, .. "IEND"u8, 0xAE, 0x42, 0x60, 0x82];
    }

    public static byte[] Gif(int width, int height) =>
        [.. "GIF89a"u8, (byte)width, (byte)(width >> 8), (byte)height, (byte)(height >> 8), 0, 0, 0, 0x3B];

    public static byte[] WebPExtended(int width, int height)
    {
        var bytes = new byte[30];
        "RIFF"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 22);
        "WEBPVP8X"u8.CopyTo(bytes.AsSpan(8));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 10);
        WriteUInt24(bytes.AsSpan(24), width - 1);
        WriteUInt24(bytes.AsSpan(27), height - 1);
        return bytes;
    }

    public static byte[] Pdf() => Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj << /Type /Catalog >> endobj\ntrailer << /Root 1 0 R >>\n%%EOF\n");

    public static byte[] WindowsExecutable() => [.. "MZ"u8, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, .. new byte[54], .. "This program cannot be run in DOS mode."u8];

    public static byte[] Svg(string body) => Encoding.UTF8.GetBytes($"""<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="10" height="10">{body}</svg>""");

    private static void WriteUInt24(Span<byte> target, int value)
    {
        target[0] = (byte)value;
        target[1] = (byte)(value >> 8);
        target[2] = (byte)(value >> 16);
    }
}
