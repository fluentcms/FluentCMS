using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentCMS.Core.Content;
using FluentCMS.Core.Media;
using FluentCMS.Core.Schema;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests.Media;

public sealed partial class MediaScreenTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52];

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_administrator_uploads_an_image_lands_on_it_and_its_bytes_come_back(string provider)
    {
        using var install = new TemporaryInstall();
        await using var database = await TemporaryDatabase.CreateAsync(provider);
        await using var host = await AdminHost.StartAsync(install, settings: database.Configuration);
        await host.CreateFirstAdministratorAsync();
        await host.SignInAsync(AdminHost.Owner);

        var uploaded = await UploadAsync(host, "drill.png", "image/png", Png, ("Folder", "products/drills"), ("Tags", "Product-Shot, hero"), ("AltText", "A cordless drill."));

        Assert.Equal(HttpStatusCode.Redirect, uploaded.StatusCode);
        var location = host.LocationOf(uploaded)!;
        Assert.Matches("^/media/[0-9a-f-]{36}\\?uploaded=true$", location);
        var id = Guid.Parse(location["/media/".Length..location.IndexOf('?')]);
        var page = await host.Client.GetStringAsync(location);
        Assert.Contains("data-testid=\"media-uploaded\"", page);
        Assert.Contains($"src=\"media/{id}/file?version=1\" alt=\"A cordless drill.\"", page);
        Assert.Contains("value=\"product-shot, hero\"", page);

        var file = await host.Client.GetAsync($"/media/{id}/file");
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal("image/png", file.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Png, await file.Content.ReadAsByteArrayAsync());
        Assert.Equal("inline", file.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("nosniff", file.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync($"/media/{Guid.NewGuid()}/file")).StatusCode);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_refused_upload_comes_back_with_its_problems_next_to_the_fields_and_stores_nothing(string provider)
    {
        using var install = new TemporaryInstall();
        await using var database = await TemporaryDatabase.CreateAsync(provider);
        await using var host = await AdminHost.StartAsync(install, settings: database.Configuration);
        var ownerId = await host.CreateFirstAdministratorAsync();
        var stored = await StoreAsync(host, ownerId, "logo.jpg", "", [], "The logo.");
        await host.SignInAsync(AdminHost.Owner);

        var mismatch = await UploadAsync(host, "notes.png", "image/png", "plain text, not a PNG"u8.ToArray(), ("Folder", "Bad Folder"), ("AltText", "Notes."));
        var noFile = await UploadAsync(host, null, null, null);
        var svg = await UploadAsync(host, "icon.svg", "image/svg+xml", "<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray());

        // The typed folder comes back to the form, and the library still lists every asset.
        Assert.StartsWith("/media?upload-error=folder_invalid", host.LocationOf(mismatch));
        var form = await host.Client.GetStringAsync(host.LocationOf(mismatch)!);
        Assert.Contains("A folder is lowercase words joined by hyphens", form);
        Assert.Contains("value=\"Bad Folder\"", form);
        Assert.Contains("Notes.", form);
        Assert.Equal([stored], Assets(form));

        var fixedFolder = await UploadAsync(host, "notes.png", "image/png", "plain text, not a PNG"u8.ToArray());
        Assert.True(fixedFolder.StatusCode == HttpStatusCode.Redirect, $"{fixedFolder.StatusCode}: {await fixedFolder.Content.ReadAsStringAsync()}");
        Assert.Equal("/media?upload-error=media_content_mismatch", host.LocationOf(fixedFolder)!.Split('&')[0]);
        Assert.Contains("does not match its type", await host.Client.GetStringAsync(host.LocationOf(fixedFolder)!));
        Assert.True(noFile.StatusCode == HttpStatusCode.Redirect, $"{noFile.StatusCode}: {await noFile.Content.ReadAsStringAsync()}");
        Assert.Equal("/media?upload-error=media_file_missing", host.LocationOf(noFile)!.Split('&')[0]);
        Assert.Equal($"/media?upload-error={MediaErrorCodes.ContentTypeInvalid}", host.LocationOf(svg)!.Split('&')[0]);
        Assert.Contains("This type of file cannot be uploaded.", await host.Client.GetStringAsync(host.LocationOf(svg)!));
        Assert.Equal(1, await QueryAsync(host, context => context.Set<FluentCMS.Infrastructure.Media.MediaAssetRow>().CountAsync()));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task The_library_filters_by_folder_subfolders_and_tag_and_marks_images_without_alt_text(string provider)
    {
        using var install = new TemporaryInstall();
        await using var database = await TemporaryDatabase.CreateAsync(provider);
        await using var host = await AdminHost.StartAsync(install, settings: database.Configuration);
        var ownerId = await host.CreateFirstAdministratorAsync();
        var drill = await StoreAsync(host, ownerId, "drill.jpg", "products/drills", ["hero"], "A drill.");
        var saw = await StoreAsync(host, ownerId, "saw.jpg", "products", [], null);
        var logo = await StoreAsync(host, ownerId, "logo.jpg", "", ["hero"], "The logo.");
        await host.SignInAsync(AdminHost.Owner);

        var all = await host.Client.GetStringAsync("/media");
        var products = await host.Client.GetStringAsync("/media?folder=products&sub=true");
        var productsOnly = await host.Client.GetStringAsync("/media?folder=products");
        var productsByOne = await host.Client.GetStringAsync("/media?folder=products&sub=1");
        var productsByOther = await host.Client.GetStringAsync("/media?folder=products&sub=yes");
        var hero = await host.Client.GetStringAsync("/media?tag=hero");

        Assert.Equal([logo, saw, drill], Assets(all));
        Assert.Equal([saw, drill], Assets(products));
        Assert.Equal([saw], Assets(productsOnly));
        Assert.Equal([saw, drill], Assets(productsByOne));
        Assert.Equal([saw], Assets(productsByOther));
        Assert.Equal([logo, drill], Assets(hero));
        Assert.Contains("data-folder=\"products/drills\"", all);
        Assert.Contains("data-tag=\"hero\"", all);
        var sawRow = all[all.IndexOf($"data-asset=\"{saw:N}\"", StringComparison.Ordinal)..];
        Assert.Contains("data-status=\"no-alt-text\"", sawRow[..sawRow.IndexOf("</tr>", StringComparison.Ordinal)]);
        Assert.Equal(1, Regex.Count(all, "data-status=\"no-alt-text\""));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Saving_details_stores_the_next_version_and_a_form_from_an_older_version_is_refused(string provider)
    {
        using var install = new TemporaryInstall();
        await using var database = await TemporaryDatabase.CreateAsync(provider);
        await using var host = await AdminHost.StartAsync(install, settings: database.Configuration);
        var ownerId = await host.CreateFirstAdministratorAsync();
        var id = await StoreAsync(host, ownerId, "saw.jpg", "", [], null);
        await host.SignInAsync(AdminHost.Owner);

        var saved = await host.SubmitAsync($"/media/{id}", "metadata", Details(1, "A circular saw."));
        var stale = await host.SubmitAsync($"/media/{id}", "metadata", Details(1, "A table saw."));
        var invalid = await host.SubmitAsync($"/media/{id}", "metadata", Details(2, "A saw.", tags: "Not A Tag!"));

        var page = await saved.Content.ReadAsStringAsync();
        Assert.Contains("data-testid=\"media-saved\"", page);
        Assert.Contains("data-version=\"2\"", page);
        Assert.Contains("A circular saw.", page);
        var conflict = await stale.Content.ReadAsStringAsync();
        Assert.Contains("data-testid=\"media-save-error\"", conflict);
        Assert.Contains("A circular saw.", conflict);
        Assert.Contains("Each tag is lowercase words", await invalid.Content.ReadAsStringAsync());
        var stored = await host.AsUserAsync<IMediaLibrary, MediaAsset>(ownerId, async library => (await library.GetAsync(id)).Value);
        Assert.Equal((2, "A circular saw."), (stored.LatestVersion, stored.Latest.Metadata.AltText));

        // Someone saves version 3 while this form, made from version 2, is refused for its own problem; its next save is a conflict.
        await host.AsUserAsync<IMediaLibrary, MediaAsset>(ownerId, async library =>
            (await library.UpdateAsync(id, 2, AssetMetadata.Create("saw.jpg", "", [], "Theirs.", null).Value)).Value);
        var refused = await host.SubmitAsync($"/media/{id}", "metadata", Details(2, "Mine.", tags: "Not A Tag!"));
        Assert.Contains("name=\"Form.BaseVersion\" value=\"2\"", await refused.Content.ReadAsStringAsync());
        var again = await host.SubmitAsync($"/media/{id}", "metadata", Details(2, "Mine."));
        Assert.Contains("details since the page was shown.", await again.Content.ReadAsStringAsync());
        var kept = await host.AsUserAsync<IMediaLibrary, MediaAsset>(ownerId, async library => (await library.GetAsync(id)).Value);
        Assert.Equal((3, "Theirs."), (kept.LatestVersion, kept.Latest.Metadata.AltText));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task The_asset_page_lists_where_it_is_used(string provider)
    {
        using var install = new TemporaryInstall();
        await using var database = await TemporaryDatabase.CreateAsync(provider);
        await using var host = await AdminHost.StartAsync(install, settings: database.Configuration);
        var ownerId = await host.CreateFirstAdministratorAsync();
        var hero = await StoreAsync(host, ownerId, "hero.jpg", "", [], "A drill on a bench.");
        var unused = await StoreAsync(host, ownerId, "spare.jpg", "", [], "A spare.");
        await host.AsUserAsync<IContentTypeDirectory, ContentType>(ownerId, async types => (await types.CreateAsync(new NewContentType(
            "Product page",
            "product-page",
            new ContentTypeDefinition([new FieldDefinition("title", "text"), new FieldDefinition("hero", "media")])), TestChangeSetTargets.ContentModel)).Value);
        var page = await host.AsUserAsync<IEntryDirectory, Entry>(ownerId, async entries => (await entries.CreateAsync(new NewEntry(
            "product-page",
            JsonSerializer.Deserialize<JsonElement>($$"""{"title": "Drills", "hero": "{{hero}}"}""")))).Value);
        await host.SignInAsync(AdminHost.Owner);

        var used = await host.Client.GetStringAsync($"/media/{hero}");
        var notUsed = await host.Client.GetStringAsync($"/media/{unused}");

        Assert.Contains($"data-entry=\"{page.Id:N}\"", used);
        Assert.Contains($"href=\"audit?type=entry&amp;id={page.Id}\"", used);
        Assert.Contains("/hero", used);
        Assert.Contains("data-testid=\"media-unused\"", notUsed);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Anonymous_requests_go_to_sign_in_and_an_upload_without_the_token_stores_nothing(string provider)
    {
        using var install = new TemporaryInstall();
        await using var database = await TemporaryDatabase.CreateAsync(provider);
        await using var host = await AdminHost.StartAsync(install, settings: database.Configuration);
        var ownerId = await host.CreateFirstAdministratorAsync();
        var id = await StoreAsync(host, ownerId, "saw.jpg", "", [], null);

        foreach (var path in new[] { "/media", $"/media/{id}", $"/media/{id}/file" })
        {
            var response = await host.Client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.StartsWith("/sign-in?ReturnUrl=", host.LocationOf(response));
        }

        await host.SignInAsync(AdminHost.Owner);
        using var form = new MultipartFormDataContent { { File("drill.png", "image/png", Png), "file" } };
        var forged = await host.Client.PostAsync("/media/upload", form);

        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
        Assert.Equal(1, await QueryAsync(host, context => context.Set<FluentCMS.Infrastructure.Media.MediaAssetRow>().CountAsync()));
    }

    [Fact]
    public async Task A_pdf_downloads_as_an_attachment()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install);
        await host.CreateFirstAdministratorAsync();
        await host.SignInAsync(AdminHost.Owner);

        var uploaded = await UploadAsync(host, "manual.pdf", "application/pdf", "%PDF-1.4\n%%EOF\n"u8.ToArray());
        var location = host.LocationOf(uploaded)!;
        var file = await host.Client.GetAsync($"{location[..location.IndexOf('?')]}/file");

        Assert.Equal("application/pdf", file.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", file.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("manual.pdf", file.Content.Headers.ContentDisposition.FileNameStar);
    }

    // A body declared over the limit is answered before it is read, with the library's own message.
    [Fact]
    public async Task An_upload_declared_over_the_limit_comes_back_with_the_too_large_message()
    {
        using var install = new TemporaryInstall();
        await using var host = await AdminHost.StartAsync(install, settings: new Dictionary<string, string?> { ["Media:MaxBytes"] = "1024" });
        await host.CreateFirstAdministratorAsync();
        await host.SignInAsync(AdminHost.Owner);

        var refused = await UploadAsync(host, "huge.jpg", "image/jpeg", MediaFiles.Jpeg(1536 * 1024));

        Assert.Equal(HttpStatusCode.Redirect, refused.StatusCode);
        Assert.Equal($"/media?upload-error={MediaErrorCodes.MediaTooLarge}", host.LocationOf(refused));
        Assert.Contains("The file is larger than the library accepts.", await host.Client.GetStringAsync(host.LocationOf(refused)!));
        Assert.Equal(0, await QueryAsync(host, context => context.Set<FluentCMS.Infrastructure.Media.MediaAssetRow>().CountAsync()));
    }

    private static Dictionary<string, string> Details(int baseVersion, string altText, string tags = "")
    {
        return new Dictionary<string, string>
        {
            ["Form.BaseVersion"] = baseVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Form.FileName"] = "saw.jpg",
            ["Form.Folder"] = "",
            ["Form.Tags"] = tags,
            ["Form.AltText"] = altText,
            ["Form.Caption"] = "",
        };
    }

    // Posts the library's upload form as a browser would, with the token from the library page.
    private static async Task<HttpResponseMessage> UploadAsync(AdminHost host, string? fileName, string? contentType, byte[]? bytes, params (string Name, string Value)[] fields)
    {
        var page = await host.Client.GetStringAsync("/media");
        var token = AntiforgeryToken().Match(page);
        Assert.True(token.Success, "The library has no anti-forgery token.");

        using var form = new MultipartFormDataContent
        {
            { new StringContent(WebUtility.HtmlDecode(token.Groups[1].Value)), "__RequestVerificationToken" },
        };
        foreach (var (name, value) in fields)
        {
            form.Add(new StringContent(value), name);
        }

        if (bytes is not null)
        {
            form.Add(File(fileName!, contentType!, bytes), "file");
        }

        return await host.Client.PostAsync("/media/upload", form);
    }

    private static ByteArrayContent File(string fileName, string contentType, byte[] bytes)
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        file.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = "file", FileName = fileName };
        return file;
    }

    private static Task<Guid> StoreAsync(AdminHost host, Guid ownerId, string fileName, string folder, string[] tags, string? altText)
    {
        return host.AsUserAsync<IMediaLibrary, Guid>(ownerId, async library => (await library.UploadAsync(new NewAsset(
            new MemoryStream(MediaFiles.Jpeg()),
            "image/jpeg",
            AssetMetadata.Create(fileName, folder, tags, altText, null).Value))).Value.Id);
    }

    private static List<Guid> Assets(string page)
    {
        return [.. AssetRow().Matches(page).Select(match => Guid.ParseExact(match.Groups[1].Value, "N"))];
    }

    private static async Task<T> QueryAsync<T>(AdminHost host, Func<FluentCmsDbContext, Task<T>> query)
    {
        await using var scope = host.App.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<FluentCmsDbContext>());
    }

    [GeneratedRegex("data-asset=\"([0-9a-f]{32})\"")]
    private static partial Regex AssetRow();

    [GeneratedRegex("name=\"__RequestVerificationToken\" value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryToken();
}
