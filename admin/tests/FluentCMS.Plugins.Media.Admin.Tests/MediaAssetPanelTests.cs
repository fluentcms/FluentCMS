using Bunit;
using FluentCMS.Admin.Api;
using FluentCMS.Plugins.Media.Admin.Api;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.Media.Admin.Tests;

public sealed class MediaAssetPanelTests : MediaTestContext
{
    private int changes;
    private bool closed;

    public MediaAssetPanelTests() =>
        Admin.Site = new MySiteInfo
        {
            Id = Guid.NewGuid(),
            Name = "Blog",
            Slug = "blog",
            DefaultLocale = "en",
            IsDefault = true,
            Locales = [new MySiteLocale { Code = "en", Position = 0, Enabled = true }, new MySiteLocale { Code = "fr", Position = 1, Enabled = true }],
        };

    [Fact]
    public void TheCaptionAndAltTextOfEachLocaleAreKeptApart()
    {
        Admin.Grant(MediaPermissions.Update);
        var id = Library.AddAsset("cat.png");
        var cut = Open(id);

        Field(cut, "Caption").Input("A cat on a mat");
        cut.Find("form.media-asset-form").Submit();
        cut.WaitForAssertion(() => Library.Text(id, "en", "caption").ShouldBe("A cat on a mat"));
        Field(cut, "Alt text").Input("A cat");
        Field(cut, "Alt text").Blur();
        cut.WaitForAssertion(() => Library.Text(id, "en", "alt").ShouldBe("A cat"));

        Field(cut, "Locale").Change("fr");
        cut.WaitForAssertion(() => Field(cut, "Caption").TextContent.ShouldBeEmpty());
        Field(cut, "Alt text").GetAttribute("value").ShouldBeNull();
        Field(cut, "Caption").Input("Un chat sur un tapis");
        cut.Find("form.media-asset-form").Submit();
        cut.WaitForAssertion(() => Library.Text(id, "fr", "caption").ShouldBe("Un chat sur un tapis"));
        Field(cut, "Alt text").Input("Un chat");
        Field(cut, "Alt text").Blur();
        cut.WaitForAssertion(() => Library.Text(id, "fr", "alt").ShouldBe("Un chat"));

        Library.Text(id, "en", "caption").ShouldBe("A cat on a mat");
        Library.Text(id, "en", "alt").ShouldBe("A cat");
        Field(cut, "Locale").Change("en");
        cut.WaitForAssertion(() => Field(cut, "Alt text").GetAttribute("value").ShouldBe("A cat"));
    }

    [Fact]
    public void TheTitleCreditTagsAndPrivateFlagAreSavedTogether()
    {
        Admin.Grant(MediaPermissions.Update);
        var id = Library.AddAsset("cat.png", tags: "pets");
        var cut = Open(id);

        Field(cut, "Title").Input("Our cat");
        Field(cut, "Credit").Input("Jo");
        Field(cut, "Tags").Input("pets, cats ,  , cats");
        cut.Find("form.media-asset-form").Submit();

        cut.WaitForAssertion(() => changes.ShouldBe(1));
        var patch = Library.Requests.Single(request => request.Method == HttpMethod.Patch).Body!.ToJsonString();
        patch.ShouldBe("""{"title":"Our cat","credit":"Jo","tags":["pets","cats"]}""");
        Announcer.Messages.ShouldContain("Our cat saved.");
        cut.Find("h2, [slot=title], .cms-drawer").TextContent.ShouldContain("Our cat");
    }

    [Fact]
    public void ASaveAfterAnotherTabChangedTheAssetShowsTheConflict()
    {
        Admin.Grant(MediaPermissions.Update);
        var id = Library.AddAsset("cat.png");
        var cut = Open(id);
        Library.ChangeElsewhere(id, "Their title");

        Field(cut, "Credit").Input("Jo");
        cut.Find("form.media-asset-form").Submit();

        cut.WaitForAssertion(() => cut.Find(".media-asset-form [role=alert]").TextContent.ShouldContain("changed elsewhere"));
        Library.Requests.ShouldNotContain(request => request.Method == HttpMethod.Patch);
        Button(cut, "Use the other change").Click();
        cut.WaitForAssertion(() => Field(cut, "Title").GetAttribute("value").ShouldBe("Their title"));
        Field(cut, "Credit").GetAttribute("value").ShouldBe("Jo");
    }

    [Fact]
    public void UsageListsTheEntriesWithTheirLabelsReadOncePerTypeAndLinksToTheirForms()
    {
        var id = Library.AddAsset("cat.png");
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var page = Guid.NewGuid();
        Library.AddUsage(id, "article", first, "Cats at home");
        Library.AddUsage(id, "article", second, "Cats abroad", locale: "fr", field: "cover");
        Library.AddUsage(id, "page", page, "About", locale: null);

        var cut = Open(id);

        cut.WaitForAssertion(() => cut.FindAll(".media-panel-usage li").Count.ShouldBe(3));
        var links = cut.FindAll(".media-panel-usage a");
        links.Select(link => link.TextContent).ShouldBe(["Cats at home", "Cats abroad", "About"]);
        links[1].GetAttribute("href").ShouldBe($"content/article/{second:D}?locale=fr");
        links[2].GetAttribute("href").ShouldBe($"content/page/{page:D}");
        Library.Requests.Count(request => request.Path.StartsWith("content/article?", StringComparison.Ordinal)).ShouldBe(1);
        Library.Requests.Count(request => request.Path.StartsWith("content/page?", StringComparison.Ordinal)).ShouldBe(1);
    }

    [Fact]
    public void DeleteShowsTheEntriesThatUseTheAssetBeforeItAsksAndTakesItOutOfThem()
    {
        Admin.Grant(MediaPermissions.Delete);
        var id = Library.AddAsset("cat.png");
        Library.AddUsage(id, "article", Guid.NewGuid(), "Cats at home");
        var cut = Open(id);
        cut.WaitForAssertion(() => cut.FindAll(".media-panel-usage li").Count.ShouldBe(1));

        Button(cut, "Delete").Click();

        cut.WaitForAssertion(() => cut.FindAll(".media-panel-step").Count.ShouldBe(1));
        var step = cut.Find(".media-panel-step");
        step.TextContent.ShouldContain("These 1 entries use this file");
        step.TextContent.ShouldContain("Cats at home");
        Library.Requests.ShouldNotContain(request => request.Method == HttpMethod.Delete);
        Button(cut, "Delete the file").Click();

        cut.WaitForAssertion(() => closed.ShouldBeTrue());
        Library.Requests.Single(request => request.Method == HttpMethod.Delete).Path.ShouldBe($"media/{id:D}?force=true");
        Library.Assets.ShouldBeEmpty();
        changes.ShouldBe(1);
        Announcer.Messages.ShouldContain("cat.png deleted.");
    }

    [Fact]
    public void AnUnusedAssetIsDeletedWithoutForce()
    {
        Admin.Grant(MediaPermissions.Delete);
        var id = Library.AddAsset("cat.png");
        var cut = Open(id);
        cut.WaitForAssertion(() => cut.Find(".media-panel-usage").TextContent.ShouldContain("No entry uses this file."));

        Button(cut, "Delete").Click();
        Button(cut, "Delete the file").Click();

        cut.WaitForAssertion(() => closed.ShouldBeTrue());
        Library.Requests.Single(request => request.Method == HttpMethod.Delete).Path.ShouldBe($"media/{id:D}");
    }

    [Fact]
    public void AReplacedAssetShowsItsNewThumbnailWithoutAReload()
    {
        Admin.Grant(MediaPermissions.Update);
        var id = Library.AddAsset("cat.png");
        var cut = Open(id);
        cut.Find(".media-focal-image img").GetAttribute("src").ShouldBe($"https://cms.example.com/media/{id:D}/thumbnail/cat.png");

        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("png", "dog.png"));

        cut.WaitForAssertion(() => cut.Find(".media-focal-image img").GetAttribute("src").ShouldBe($"https://cms.example.com/media/{id:D}/thumbnail/dog.png?v=2"));
        cut.Find(".media-panel-facts").TextContent.ShouldContain("dog.png");
        changes.ShouldBe(1);
        Announcer.Messages.ShouldContain("The file of dog.png was replaced.");
    }

    [Fact]
    public void APrivateAssetDownloadsThroughASignedUrl()
    {
        var id = Library.AddAsset("contract.pdf", mimeType: "application/pdf");
        Library.Asset(id)["isPrivate"] = true;
        var cut = Open(id);
        cut.FindAll(".media-panel-link").ShouldBeEmpty();

        Button(cut, "Download").Click();

        var navigation = Services.GetRequiredService<NavigationManager>();
        cut.WaitForAssertion(() => navigation.Uri.ShouldBe($"https://cms.example.com/media/{id:D}/original/file?exp=1&sig=abc"));
        Library.Requests.ShouldContain(request => request.Method == HttpMethod.Post && request.Path == $"media/{id:D}/download-url");
    }

    [Fact]
    public void APublicAssetDownloadsFromItsOriginal()
    {
        var id = Library.AddAsset("cat.png");

        var cut = Open(id);

        cut.Find(".media-panel-link").GetAttribute("href").ShouldBe($"https://cms.example.com/media/{id:D}/original/cat.png");
    }

    [Fact]
    public async Task AnAssetMovesToAnotherFolder()
    {
        Admin.Grant(MediaPermissions.Update);
        var photos = Library.AddFolder("Photos");
        var id = Library.AddAsset("cat.png");
        var folders = await new MediaApi(Library).FoldersAsync(CancellationToken.None);
        var cut = Open(id, folders);

        Button(cut, "Move").Click();
        Field(cut, "Move to").Change(photos.ToString("D"));
        cut.Find(".media-panel-step fluent-button").Click();

        cut.WaitForAssertion(() => Library.FolderOf(id).ShouldBe(photos));
        changes.ShouldBe(1);
        Announcer.Messages.ShouldContain("cat.png moved to Photos.");
    }

    [Fact]
    public void AReaderSeesTheDetailButChangesNothing()
    {
        var id = Library.AddAsset("cat.png");

        var cut = Open(id);

        Field(cut, "Title").HasAttribute("readonly").ShouldBeTrue();
        Field(cut, "Alt text").HasAttribute("readonly").ShouldBeTrue();
        cut.Find(".media-focal-sliders").HasAttribute("disabled").ShouldBeTrue();
        HasButton(cut, "Save").ShouldBeFalse();
        HasButton(cut, "Move").ShouldBeFalse();
        HasButton(cut, "Delete").ShouldBeFalse();
        cut.FindComponents<InputFile>().ShouldBeEmpty();
        HasButton(cut, "Download").ShouldBeFalse();
        cut.FindAll(".media-panel-link").Count.ShouldBe(1);
    }

    private IRenderedComponent<MediaAssetPanel> Open(Guid id, IReadOnlyList<MediaFolderInfo>? folders = null)
    {
        var asset = new MediaApi(Library).GetAsync(id, null, CancellationToken.None).GetAwaiter().GetResult();
        Library.Requests.Clear();
        var cut = Render<MediaAssetPanel>(parameters => parameters
            .Add(panel => panel.Asset, asset)
            .Add(panel => panel.Open, true)
            .Add(panel => panel.Folders, folders ?? [])
            .Add(panel => panel.OnChanged, () => changes++)
            .Add(panel => panel.OnClose, () => closed = true));
        // The usage is read after the asset; a click while that read renders could reach a handler it replaced.
        cut.WaitForAssertion(() => cut.Find(".media-panel-usage").TextContent.ShouldNotContain("Reading"));
        return cut;
    }
}
