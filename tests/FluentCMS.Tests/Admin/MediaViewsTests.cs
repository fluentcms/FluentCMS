using Bunit;
using FluentCMS.Admin.Components.Pages;
using FluentCMS.Admin.Media;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Content;
using FluentCMS.Core.Media;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Tests.Admin;

public sealed class MediaViewsTests
{
    [Fact]
    public void The_library_lists_the_newest_first_with_previews_status_and_filters()
    {
        var library = new FakeLibrary();
        using var context = Context(library);

        var cut = context.Render<MediaLibraryView>();

        var rows = cut.FindAll("[data-asset]");
        Assert.Equal([library.Pdf.Id, library.NoAlt.Id, library.Drill.Id], rows.Select(row => Guid.ParseExact(row.GetAttribute("data-asset")!, "N")));
        Assert.Null(rows[0].QuerySelector("img"));
        Assert.Equal($"media/{library.NoAlt.Id}/file", rows[1].QuerySelector("img")!.GetAttribute("src"));
        Assert.Equal("", rows[1].QuerySelector("img")!.GetAttribute("alt"));
        Assert.NotNull(rows[1].QuerySelector("[data-status=no-alt-text]"));
        Assert.Null(rows[2].QuerySelector("[data-status=no-alt-text]"));
        Assert.NotNull(rows[2].QuerySelector("[data-status=published]"));
        Assert.Null(rows[0].QuerySelector("[data-status=no-alt-text]"));
        Assert.Equal(["", "products", "products/drills"], cut.FindAll("[data-folder]").Select(link => link.GetAttribute("data-folder")));
        Assert.Equal(["hero", "product-shot"], cut.FindAll("[data-tag]").Select(link => link.GetAttribute("data-tag")));
        Assert.Equal("page", cut.Find("[data-folder='']").GetAttribute("aria-current"));
        Assert.Equal("rove", cut.Find("tbody").GetAttribute("data-ui"));
    }

    [Fact]
    public void A_folder_or_tag_in_the_address_filters_the_list()
    {
        var library = new FakeLibrary();
        using var context = Context(library);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/media?folder=products&sub=true");

        var cut = context.Render<MediaLibraryView>();

        Assert.Equal(new MediaQuery("products", true, null), library.Queries[^1]);
        Assert.Equal("page", cut.Find("[data-folder=products]").GetAttribute("aria-current"));
    }

    [Fact]
    public void The_upload_form_posts_to_the_endpoint_and_shows_a_refused_upload_next_to_its_fields()
    {
        var library = new FakeLibrary();
        using var context = Context(library);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo("/media?upload-error=media_content_mismatch,folder_invalid&upload-folder=Bad&upload-alt=A%20drill.&upload-dropped=upload-caption");

        var cut = context.Render<MediaLibraryView>();

        var form = cut.Find("[data-testid=media-upload] form");
        Assert.Equal("media/upload", form.GetAttribute("action"));
        Assert.Equal("multipart/form-data", form.GetAttribute("enctype"));
        Assert.Contains("image/png", cut.Find("input[type=file]").GetAttribute("accept"));
        Assert.Contains("does not match its type", form.TextContent);
        Assert.Contains("A folder is lowercase words", form.TextContent);
        Assert.Equal("Bad", cut.Find("input[name=Folder]").GetAttribute("value"));
        Assert.Equal("A drill.", cut.Find("textarea[name=AltText]").GetAttribute("value"));
        Assert.Contains("This was too long to bring back here.", form.TextContent);
        Assert.Empty(cut.FindAll("[data-testid=media-upload-error]"));

        // The upload's fields never filter the list.
        Assert.Equal(new MediaQuery(), Assert.Single(library.Queries));
    }

    [Fact]
    public void The_asset_page_shows_the_preview_the_details_form_and_where_it_is_used()
    {
        var library = new FakeLibrary();
        using var context = Context(library);

        var cut = context.Render<MediaAssetView>(page => page.Add(p => p.Id, library.Drill.Id));

        Assert.Equal("drill.jpg", cut.Find("h1").TextContent);
        Assert.Equal($"media/{library.Drill.Id}/file?version=2", cut.Find("[data-testid=media-preview]").GetAttribute("src"));
        Assert.Equal("A cordless drill.", cut.Find("[data-testid=media-preview]").GetAttribute("alt"));
        Assert.Equal("2", cut.Find("input[name='Form.BaseVersion']").GetAttribute("value"));
        Assert.Equal("hero, product-shot", cut.Find("input[name='Form.Tags']").GetAttribute("value"));
        Assert.Equal("products/drills", cut.Find("input[name='Form.Folder']").GetAttribute("value"));
        var usage = cut.Find("[data-entry]");
        Assert.Contains("/hero", usage.TextContent);
        Assert.Contains("product-page", usage.TextContent);
        Assert.Equal(
            ["audit?type=entry&id=", "audit?type=entity&id="],
            cut.FindAll("[data-testid=usage-history]").Select(link => link.GetAttribute("href")![..(link.GetAttribute("href")!.IndexOf("id=", StringComparison.Ordinal) + 3)]));
        Assert.Empty(cut.FindAll("[data-testid=media-no-alt-text]"));
    }

    [Fact]
    public void An_image_without_alt_text_says_so_on_its_page()
    {
        var library = new FakeLibrary();
        using var context = Context(library);

        var cut = context.Render<MediaAssetView>(page => page.Add(p => p.Id, library.NoAlt.Id));

        Assert.NotNull(cut.Find("[data-testid=media-no-alt-text]"));
        Assert.Equal("", cut.Find("[data-testid=media-preview]").GetAttribute("alt"));
        Assert.NotNull(cut.Find("[data-testid=media-unused]"));
    }

    [Fact]
    public void Uses_logical_utilities_only()
    {
        var library = new FakeLibrary();
        using var context = Context(library);

        var list = context.Render<MediaLibraryView>();
        var asset = context.Render<MediaAssetView>(page => page.Add(p => p.Id, library.Drill.Id));

        Assert.DoesNotContain(list.FindAll("[class]").Concat(asset.FindAll("[class]")).SelectMany(element => element.ClassList), Utilities.IsPhysical);
    }

    [Theory]
    [InlineData("hero, Product-Shot ,, ", new[] { "hero", "Product-Shot" })]
    [InlineData("", new string[0])]
    [InlineData(null, new string[0])]
    public void Tags_are_split_on_commas(string? typed, string[] tags)
    {
        Assert.Equal(tags, MediaMetadataFields.SplitTags(typed));
    }

    [Fact]
    public void A_refused_upload_returns_its_codes_and_fields_and_blank_texts_mean_none()
    {
        var form = new MediaUploadForm { Folder = "Bad", Tags = "a, b", AltText = " ", Caption = "Shown." };

        var query = form.ReturnQuery([new Error("folder_invalid", "x"), new Error("folder_invalid", "y"), new Error("tag_invalid", "z")]);
        var metadata = new MediaUploadForm { Folder = "products", Tags = "Hero", AltText = " " }.ToMetadata("drill.jpg").Value;

        Assert.Equal("folder_invalid,tag_invalid", query["upload-error"]);
        Assert.Equal(("Bad", "a, b", " ", "Shown."), (query["upload-folder"], query["upload-tags"], query["upload-alt"], query["upload-caption"]));
        Assert.False(query.ContainsKey("upload-dropped"));
        Assert.Equal(("products", "hero", null), (metadata.Folder, Assert.Single(metadata.Tags), metadata.AltText));
    }

    [Fact]
    public void A_text_too_long_for_the_return_address_is_dropped_and_named()
    {
        var form = new MediaUploadForm { Folder = "ok", AltText = new string('ä', 1000) };

        var query = form.ReturnQuery([new Error("tag_invalid", "x")]);

        Assert.Equal("ok", query["upload-folder"]);
        Assert.False(query.ContainsKey("upload-alt"));
        Assert.Equal("upload-alt", query["upload-dropped"]);
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("yes", false)]
    public void The_subfolder_flag_takes_one_or_true(string sub, bool subfolders)
    {
        var library = new FakeLibrary();
        using var context = Context(library);
        context.Services.GetRequiredService<NavigationManager>().NavigateTo($"/media?folder=products&sub={sub}");

        context.Render<MediaLibraryView>();

        Assert.Equal(new MediaQuery("products", subfolders, null), library.Queries[^1]);
    }

    private static AdminTestContext Context(FakeLibrary library)
    {
        var context = new AdminTestContext();
        context.Services.AddSingleton<IMediaLibrary>(library);
        context.Services.AddSingleton<IWhereUsed>(new FakeUsage(library));
        context.Services.AddSingleton<IContentTypeDirectory>(new FakeContentTypes());
        return context;
    }

    private sealed class FakeLibrary : IMediaLibrary
    {
        public long MaxBytes => 50 * 1024 * 1024;

        public MediaAssetSummary Drill { get; } = new(Guid.CreateVersion7(), 2, 1, "drill.jpg", "image/jpeg", 2048, "products/drills", ["hero", "product-shot"], true);

        public MediaAssetSummary NoAlt { get; } = new(Guid.CreateVersion7(), 1, null, "saw.png", "image/png", 512, "products", [], false);

        public MediaAssetSummary Pdf { get; } = new(Guid.CreateVersion7(), 1, null, "manual.pdf", "application/pdf", 3 * 1024 * 1024, "", ["hero"], false);

        public List<MediaQuery> Queries { get; } = [];

        public Task<IReadOnlyList<MediaAssetSummary>> ListAsync(MediaQuery query, CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            IReadOnlyList<MediaAssetSummary> all = [Drill, NoAlt, Pdf];
            return Task.FromResult(all);
        }

        public Task<IReadOnlyList<string>> FoldersAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<string>>(["", "products", "products/drills"]);
        }

        public Task<Result<MediaAsset>> GetAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var summary = new[] { Drill, NoAlt, Pdf }.SingleOrDefault(asset => asset.Id == id);
            if (summary is null)
            {
                return Task.FromResult(Result<MediaAsset>.Failure(MediaErrorCodes.AssetNotFound, "No such asset."));
            }

            var metadata = new AssetMetadata(summary.FileName, summary.Folder, summary.Tags, summary.HasAltText ? "A cordless drill." : null, null);
            var version = new MediaAssetVersion(id, summary.LatestVersion, new MediaBlob(summary.ContentType, summary.Length, "00", "local", "key"), metadata, DateTimeOffset.UnixEpoch);
            return Task.FromResult(Result<MediaAsset>.Success(new MediaAsset(id, Guid.NewGuid(), summary.LatestVersion, summary.PublishedVersion, version)));
        }

        public Task<Result<MediaAsset>> UploadAsync(NewAsset asset, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<MediaAsset>> UpdateAsync(Guid id, int baseVersion, AssetMetadata metadata, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<MediaAssetVersion>> GetVersionAsync(Guid id, int version, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<MediaAssetVersion?>> LiveVersionAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<MediaContent>> OpenAsync(Guid id, int? version = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    // Every type is a plain content type, so its items are entries in the audit log.
    private sealed class FakeContentTypes : IContentTypeDirectory
    {
        public Task<Result<ContentType>> GetAsync(string slug, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ContentType.Create(Guid.NewGuid(), slug, slug, new ContentTypeDefinition([], isEntity: slug == "supplier"), new FieldTypeRegistry([])));
        }

        public Task<IReadOnlyList<ContentTypeSummary>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<ContentType>> GetPublishedAsync(string slug, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<ContentType>> GetVersionAsync(string slug, int version, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<ContentType>> CreateAsync(NewContentType contentType, ChangeSetTarget changeSet, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<ContentType>> ReviseAsync(string slug, int baseVersion, ContentTypeDefinition definition, ChangeSetTarget changeSet, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeUsage(FakeLibrary library) : IWhereUsed
    {
        public Task<IReadOnlyList<EntryUsage>> ForMediaAsync(Guid assetId, CancellationToken cancellationToken = default)
        {
            IReadOnlyList<EntryUsage> usage = assetId == library.Drill.Id
                ? [new EntryUsage(Guid.NewGuid(), "product-page", 3, true, true, ["/hero"]), new EntryUsage(Guid.NewGuid(), "supplier", 1, true, false, ["/logo/value"])]
                : [];
            return Task.FromResult(usage);
        }

        public Task<Result<IReadOnlyList<EntryUsage>>> ForEntryAsync(Guid entryId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Result<IReadOnlyList<EntryUsage>>> ForEntityFieldAsync(Guid entityId, string field, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
