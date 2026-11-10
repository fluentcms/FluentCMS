using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Content;
using FluentCMS.Plugins.Media;
using FluentCMS.Settings;
using FluentCMS.Sites;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Mcp.Tests.McpTesting;
using static FluentCMS.Plugins.Mcp.Tests.McpToolsTesting;

namespace FluentCMS.Plugins.Mcp.Tests;

// The media tools of spec 5.4, over the media plugin's service: listed where the plugin is enabled and the caller holds
// the permission, and an upload by URL reaches public hosts only.
public sealed class McpMediaToolTests(McpToolsFixture fixture) : IClassFixture<McpToolsFixture>
{
    private const string Media = "/api/management/v1/media";

    [Fact]
    public async Task AnAssetIsUploadedFromBase64UpdatedReadListedAndSearched()
    {
        using var http = await KeyAsync(fixture.Host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);
        var name = $"harbour-{Guid.NewGuid():N}";

        var uploaded = await OkAsync(client, McpMediaTools.Upload, new { fileName = $"{name}.png", data = Convert.ToBase64String(Png(3, 2)) });
        var id = uploaded.GetProperty("items")[0].GetProperty("id").GetGuid();
        var updated = await OkAsync(client, McpMediaTools.Update, new { id, title = $"Dusk {name}", alt = "Boats", tags = new[] { "boats", $"t{name}" }, focalPoint = new { x = 0.25, y = 0.5 } });
        var read = await OkAsync(client, McpMediaTools.Get, new { id });
        var listed = await OkAsync(client, McpMediaTools.List, new { type = "image/*", tag = $"t{name}", pageSize = 5 });
        var byTitle = await OkAsync(client, McpMediaTools.Search, new { q = $"DUSK {name}" });
        var byFileName = await OkAsync(client, McpMediaTools.Search, new { q = $"{name}.PNG" });
        var byTag = await OkAsync(client, McpMediaTools.Search, new { q = $"t{name}", type = "image/png" });
        var byNothing = await OkAsync(client, McpMediaTools.Search, new { q = $"nothing-{name}" });

        uploaded.GetProperty("items")[0].ShouldSatisfyAllConditions(
            asset => asset.GetProperty("fileName").GetString().ShouldBe($"{name}.png"),
            asset => asset.GetProperty("mimeType").GetString().ShouldBe("image/png"),
            asset => asset.GetProperty("width").GetInt32().ShouldBe(3));
        updated.GetProperty("title").GetString().ShouldBe($"Dusk {name}");
        updated.GetProperty("focalPoint").GetProperty("x").GetDouble().ShouldBe(0.25);
        read.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()).ShouldBe(["boats", $"t{name}"]);
        JsonNode.DeepEquals(JsonNode.Parse(read.GetRawText()), await http.GetFromJsonAsync<JsonNode>(new Uri($"{Media}/{id:D}", UriKind.Relative), Token)).ShouldBeTrue();
        foreach (var page in (JsonElement[])[listed, byTitle, byFileName, byTag])
        {
            page.GetProperty("items").EnumerateArray().Select(asset => asset.GetProperty("id").GetGuid()).ShouldBe([id]);
            page.GetProperty("total").GetInt64().ShouldBe(1);
        }

        byNothing.GetProperty("items").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task AnUploadFromAPublicUrlStoresTheFileAndFollowsARedirectToAPublicHost()
    {
        using var http = await KeyAsync(fixture.Host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);
        var name = $"quay-{Guid.NewGuid():N}";
        fixture.Downloads.Serve($"https://cdn.example.com/photos/{name}.png?size=large", Png(4, 4));
        fixture.Downloads.Redirect($"https://www.example.com/{name}/latest", $"https://cdn.example.com/photos/{name}.png?size=large");

        var direct = await OkAsync(client, McpMediaTools.Upload, new { url = $"https://cdn.example.com/photos/{name}.png?size=large" });
        var redirected = await OkAsync(client, McpMediaTools.Upload, new { url = $"https://www.example.com/{name}/latest", fileName = "latest.png" });

        direct.GetProperty("items")[0].ShouldSatisfyAllConditions(
            asset => asset.GetProperty("fileName").GetString().ShouldBe($"{name}.png"),
            asset => asset.GetProperty("mimeType").GetString().ShouldBe("image/png"),
            asset => asset.GetProperty("height").GetInt32().ShouldBe(4));
        redirected.GetProperty("items")[0].GetProperty("fileName").GetString().ShouldBe("latest.png");
        fixture.Downloads.Requested.ShouldContain($"https://www.example.com/{name}/latest");
    }

    [Theory]
    [InlineData("http://cdn.example.com/{0}.png", "https")]
    [InlineData("ftp://cdn.example.com/{0}.png", "https")]
    [InlineData("cdn.example.com/{0}.png", "absolute")]
    [InlineData("https://user:secret@cdn.example.com/{0}.png", "user name")]
    [InlineData("https://127.0.0.1/{0}.png", "not a public address")]
    [InlineData("https://10.1.2.3:8443/{0}.png", "not a public address")]
    [InlineData("https://169.254.169.254/latest/meta-data/{0}", "not a public address")]
    [InlineData("https://[::1]/{0}.png", "not a public address")]
    [InlineData("https://[::ffff:192.168.1.1]/{0}.png", "not a public address")]
    public async Task AUrlThatIsNotAPublicHttpsAddressIsRefusedAndNothingIsFetched(string pattern, string reason)
    {
        using var http = await KeyAsync(fixture.Host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);
        var marker = Guid.NewGuid().ToString("N");
        var url = string.Format(System.Globalization.CultureInfo.InvariantCulture, pattern, marker);

        var refused = await CallAsync(client, McpMediaTools.Upload, new { url, fileName = "a.png" });

        refused.IsError.ShouldBe(true);
        Code(refused).ShouldBe("FCMS1614");
        Text(refused).ShouldContain(reason);
        fixture.Downloads.Requested.ShouldNotContain(requested => requested.Contains(marker, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ARedirectToAPrivateAddressToHttpOrWithoutEndIsNotFollowed()
    {
        using var http = await KeyAsync(fixture.Host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);
        var marker = Guid.NewGuid().ToString("N");
        fixture.Downloads.Redirect($"https://cdn.example.com/{marker}/inside", $"https://192.168.1.10/{marker}/secret.png");
        fixture.Downloads.Redirect($"https://cdn.example.com/{marker}/plain", $"http://cdn.example.com/{marker}/plain.png");
        fixture.Downloads.Redirect($"https://cdn.example.com/{marker}/loop", $"/{marker}/loop");

        var inside = await CallAsync(client, McpMediaTools.Upload, new { url = $"https://cdn.example.com/{marker}/inside", fileName = "a.png" });
        var plain = await CallAsync(client, McpMediaTools.Upload, new { url = $"https://cdn.example.com/{marker}/plain", fileName = "a.png" });
        var loop = await CallAsync(client, McpMediaTools.Upload, new { url = $"https://cdn.example.com/{marker}/loop", fileName = "a.png" });

        Code(inside).ShouldBe("FCMS1614");
        Text(inside).ShouldContain("not a public address");
        Code(plain).ShouldBe("FCMS1614");
        Code(loop).ShouldBe("FCMS1614");
        Text(loop).ShouldContain("redirects more than");
        fixture.Downloads.Requested.ShouldNotContain(requested => requested.Contains("192.168.1.10", StringComparison.Ordinal) || requested.StartsWith("http://", StringComparison.Ordinal));
        fixture.Downloads.Requested.Count(requested => requested == $"https://cdn.example.com/{marker}/loop").ShouldBe(McpDownloads.MaxRedirects + 1);
    }

    [Fact]
    public async Task AUrlThatDoesNotAnswerWithAFileIsAFailureOfTheFetch()
    {
        using var http = await KeyAsync(fixture.Host);
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        var missing = await CallAsync(client, McpMediaTools.Upload, new { url = $"https://cdn.example.com/{Guid.NewGuid():N}/missing.png" });
        var neither = await CallAsync(client, McpMediaTools.Upload, new { fileName = "a.png" });
        var both = await CallAsync(client, McpMediaTools.Upload, new { fileName = "a.png", url = "https://cdn.example.com/a.png", data = "AAAA" });
        var notBase64 = await CallAsync(client, McpMediaTools.Upload, new { fileName = "a.png", data = "not base64!" });

        Code(missing).ShouldBe("FCMS1615");
        Text(missing).ShouldContain("404");
        Code(neither).ShouldBe("FCMS2108");
        Code(both).ShouldBe("FCMS2108");
        Code(notBase64).ShouldBe("FCMS2108");
        Text(notBase64).ShouldContain("- data: ");
    }

    [Fact]
    public async Task AnUploadFromAUrlKeepsTheUploadLimitsOfTheMediaSettingsOfTheSite()
    {
        var host = fixture.Host;
        var site = await CreateSiteAsync(host);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            using var system = SystemOperation.Begin(scope.ServiceProvider.GetRequiredService<IOperationContextAccessor>(), Origin.Seed, new SiteId(site));
            await scope.ServiceProvider.GetRequiredService<ISettings<MediaSettings>>().SetAsync(new SiteId(site), new MediaSettings { MaxFileSize = 40, AllowedTypes = ["image/png"] }, Token);
        }

        using var http = await KeyAsync(host);
        await using var client = await ConnectAsync(http, site, InitializeProtocol);
        var marker = Guid.NewGuid().ToString("N");
        fixture.Downloads.Serve($"https://cdn.example.com/{marker}/large.png", [.. Png(2, 2), .. new byte[100]]);
        fixture.Downloads.Serve($"https://cdn.example.com/{marker}/small.gif", [.. "GIF89a"u8, 1, 0, 1, 0, 0, 0, 0, 0x3B]);

        var tooLarge = await CallAsync(client, McpMediaTools.Upload, new { url = $"https://cdn.example.com/{marker}/large.png" });
        var notAllowed = await CallAsync(client, McpMediaTools.Upload, new { url = $"https://cdn.example.com/{marker}/small.gif" });

        Code(tooLarge).ShouldBe("FCMS1582");
        Code(notAllowed).ShouldBe("FCMS1581");
    }

    // The media service checks the permission before it reads the file, and the URL is fetched only when it is read.
    [Fact]
    public async Task ACallerWithoutTheUploadPermissionDoesNotSeeTheUploadToolAndMakesTheServerFetchNothing()
    {
        using var http = await RoleAsync(fixture.Host, Grant("media.read"));
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);
        var url = $"https://cdn.example.com/{Guid.NewGuid():N}/quay.png";
        fixture.Downloads.Serve(url, Png(1, 1));

        var tools = await ToolsAsync(client);
        var refused = await CallAsync(client, McpMediaTools.Upload, new { url });
        var update = await CallAsync(client, McpMediaTools.Update, new { id = Guid.NewGuid(), title = "No" });

        Of(tools, "media_").ShouldBe([McpMediaTools.Get, McpMediaTools.List, McpMediaTools.Search]);
        Code(refused).ShouldBe("FCMS3103");
        Code(update).ShouldBe("FCMS3103");
        fixture.Downloads.Requested.ShouldNotContain(url);
    }

    // A grant limited to folders still uploads into them, so its holder is offered the tool.
    [Fact]
    public async Task AGrantLimitedToFoldersStillListsTheToolsOfItsPermission()
    {
        using var admin = await KeyAsync(fixture.Host);
        using var created = await admin.PostAsJsonAsync(new Uri($"{Media}/folders", UriKind.Relative), new { name = $"team-{Guid.NewGuid():N}" }, Token);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var folder = (await JsonAsync(created))["id"]!.GetValue<Guid>();
        using var http = await RoleAsync(fixture.Host, new { permission = "media.upload", constraints = new { resources = new[] { folder.ToString("D") } } });
        await using var client = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        var tools = await ToolsAsync(client);
        var uploaded = await OkAsync(client, McpMediaTools.Upload, new { fileName = "team.png", data = Convert.ToBase64String(Png(1, 1)), folderId = folder });

        Of(tools, "media_").ShouldBe([McpMediaTools.Upload]);
        uploaded.GetProperty("items")[0].GetProperty("folderId").GetGuid().ShouldBe(folder);
    }

    [Fact]
    public async Task TheMediaToolsAreAbsentWhereTheMediaPluginIsDisabledForTheSite()
    {
        var host = fixture.Host;
        var site = await CreateSiteAsync(host);
        await SetEnabledAsync(host, McpMediaTools.MediaPlugin, site, false);
        using var http = await KeyAsync(host);
        await using var inSite = await ConnectAsync(http, site, InitializeProtocol);
        await using var inDefault = await ConnectAsync(http, protocolVersion: InitializeProtocol);

        var tools = await ToolsAsync(inSite);
        var called = await Should.ThrowAsync<McpProtocolException>(async () => await CallAsync(inSite, McpMediaTools.List));

        Of(tools, "media_").ShouldBeEmpty();
        Of(tools, "post_").ShouldNotBeEmpty();
        called.Message.ShouldContain("FCMS1600");
        Of(await ToolsAsync(inDefault), "media_").ShouldBe([McpMediaTools.Get, McpMediaTools.List, McpMediaTools.Search, McpMediaTools.Update, McpMediaTools.Upload]);
    }

    private static byte[] Png(int width, int height)
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
}
