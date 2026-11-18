using Bunit;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Components;
using FluentCMS.Plugins.Media.Admin.Api;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
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
        cut.WaitForAssertion(() => Field(cut, "Caption").GetAttribute("value").ShouldBeNull());
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

        // The other change is what shows, and nothing is left to save over it.
        Field(cut, "Credit").GetAttribute("value").ShouldBeNull();
        cut.Find(".media-asset-form fluent-button").HasAttribute("disabled").ShouldBeTrue();
    }

    [Fact]
    public void ASaveAfterAMoveIsBasedOnTheMove()
    {
        Admin.Grant(MediaPermissions.Update);
        var photos = Library.AddFolder("Photos");
        var id = Library.AddAsset("cat.png");
        var cut = Open(id, [new MediaFolderInfo { Id = photos, Name = "Photos" }]);

        Button(cut, "Move").Click();
        Field(cut, "Move to").Change(photos.ToString("D"));
        cut.Find(".media-panel-step fluent-button").Click();
        cut.WaitForAssertion(() => Library.FolderOf(id).ShouldBe(photos));
        Field(cut, "Alt text").Input("A cat");
        Field(cut, "Alt text").Blur();

        cut.WaitForAssertion(() => Library.Text(id, "en", "alt").ShouldBe("A cat"));
        cut.FindAll("[role=alert]").ShouldBeEmpty();
    }

    [Fact]
    public void SavesOfDifferentPartsInARowAreNoConflict()
    {
        Admin.Grant(MediaPermissions.Update);
        var id = Library.AddAsset("cat.png");
        var cut = Open(id);

        Field(cut, "Alt text").Input("A cat");
        Field(cut, "Alt text").Blur();
        cut.WaitForAssertion(() => Library.Text(id, "en", "alt").ShouldBe("A cat"));
        Field(cut, "Title").Input("Our cat");
        cut.Find("form.media-asset-form").Submit();

        cut.WaitForAssertion(() => Library.Asset(id)["title"]!.GetValue<string>().ShouldBe("Our cat"));
        cut.FindAll("[role=alert]").ShouldBeEmpty();
    }

    [Fact]
    public async Task SavesThatOverlapGoOneAfterAnotherAndTheLatestPointWins()
    {
        Admin.Grant(MediaPermissions.Update);
        var id = Library.AddAsset("cat.png");
        var cut = Open(id);
        var release = new TaskCompletionSource();
        Library.BeforeAnswer = request => request.Method == HttpMethod.Patch ? release.Task : Task.CompletedTask;

        // The alt text is saved as the field loses focus to a click on the image; two more clicks follow while it saves.
        Field(cut, "Alt text").Input("A cat");
        var altSaved = Field(cut, "Alt text").BlurAsync(new FocusEventArgs());
        var firstClick = cut.Find(".media-focal-image img").ClickAsync(new MouseEventArgs { OffsetX = 32, OffsetY = 24 });
        var secondClick = cut.Find(".media-focal-image img").ClickAsync(new MouseEventArgs { OffsetX = 288, OffsetY = 216 });
        Library.Requests.Count(request => request.Method == HttpMethod.Patch).ShouldBe(0);
        release.SetResult();
        await Task.WhenAll(altSaved, firstClick, secondClick);

        cut.WaitForAssertion(() => Library.Requests.Count(request => request.Method == HttpMethod.Patch).ShouldBe(2));
        cut.WaitForAssertion(() => cut.Find(".media-focal-marker").GetAttribute("style").ShouldBe("left:90%;top:90%"));
        Library.Text(id, "en", "alt").ShouldBe("A cat");
        Library.Asset(id)["focalPoint"]!.ToJsonString().ShouldBe("""{"x":0.9,"y":0.9}""");
        cut.FindAll("[role=alert]").ShouldBeEmpty();
    }

    [Fact]
    public async Task ALocaleChosenWhileTheUsageIsReadKeepsTheUsage()
    {
        var id = Library.AddAsset("cat.png");
        Library.AddUsage(id, "article", Guid.NewGuid(), "Cats at home");
        var release = new TaskCompletionSource();
        Library.BeforeAnswer = request => request.Path.EndsWith("/usage", StringComparison.Ordinal) ? release.Task : Task.CompletedTask;
        var asset = await new MediaApi(Library).GetAsync(id, null, CancellationToken.None);
        Admin.Grant(MediaPermissions.Delete);
        var cut = Render<MediaAssetPanel>(parameters => parameters.Add(panel => panel.Asset, asset).Add(panel => panel.Open, true));
        cut.WaitForAssertion(() => cut.FindAll(".media-asset-form").Count.ShouldBe(1));

        Field(cut, "Locale").Change("fr");
        cut.WaitForAssertion(() => Library.Requests.ShouldContain(request => request.Method == HttpMethod.Get && request.Locale == "fr"));
        release.SetResult();

        cut.WaitForAssertion(() => cut.Find(".media-panel-usage").TextContent.ShouldContain("Cats at home"));
        Button(cut, "Delete").Click();
        cut.WaitForAssertion(() => cut.FindAll(".media-panel-step fluent-button")[0].HasAttribute("disabled").ShouldBeFalse());
    }

    [Fact]
    public async Task AReplaceThatEndsAfterTheUserOpenedAnotherAssetChangesNothingInIt()
    {
        Admin.Grant(MediaPermissions.Update, MediaPermissions.Delete);
        var first = Library.AddAsset("cat.png");
        var second = Library.AddAsset("dog.png");
        var cut = Open(first);
        var release = new TaskCompletionSource();
        var reached = new TaskCompletionSource();
        Library.BeforeAnswer = request =>
        {
            if (!request.Path.EndsWith("/replace", StringComparison.Ordinal))
            {
                return Task.CompletedTask;
            }

            reached.TrySetResult();
            return release.Task;
        };

        // The upload waits for its handler, which waits for the replace this test holds back.
        var input = cut.FindComponent<InputFile>();
        var upload = Task.Run(() => input.UploadFiles(InputFileContent.CreateFromText("png", "big.png")), Xunit.TestContext.Current.CancellationToken);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(5), Xunit.TestContext.Current.CancellationToken);
        cut.Render(parameters => parameters.Add(panel => panel.Open, false));
        var dog = await new MediaApi(Library).GetAsync(second, null, CancellationToken.None);
        cut.Render(parameters => parameters.Add(panel => panel.Asset, dog).Add(panel => panel.Open, true));
        cut.WaitForAssertion(() => cut.Find(".media-panel-facts").TextContent.ShouldContain("dog.png"));
        release.SetResult();
        await upload;

        cut.WaitForAssertion(() => changes.ShouldBe(1));
        cut.Find(".media-panel-facts").TextContent.ShouldContain("dog.png");
        cut.Find(".media-panel-facts").TextContent.ShouldNotContain("big.png");
        Announcer.Messages.ShouldNotContain(message => message.Contains("replaced", StringComparison.Ordinal));
        Library.Asset(first)["fileName"]!.GetValue<string>().ShouldBe("big.png");
    }

    [Fact]
    public void ASwitchOfLocaleWithAnUnsavedCaptionAsksAndKeepsTheValuesThatAreNotPerLocale()
    {
        Admin.Grant(MediaPermissions.Update);
        var id = Library.AddAsset("cat.png");
        var cut = Open(id);
        Field(cut, "Title").Input("Our cat");
        Field(cut, "Caption").Input("On a mat");

        Field(cut, "Locale").Change("fr");
        cut.WaitForAssertion(() => cut.FindComponent<CmsDialog>().Instance.Open.ShouldBeTrue());
        Library.Requests.ShouldNotContain(request => request.Locale == "fr");
        Button(cut, "Keep editing").Click();
        cut.WaitForAssertion(() => cut.FindComponent<CmsDialog>().Instance.Open.ShouldBeFalse());
        Field(cut, "Locale").GetAttribute("value").ShouldBe("en");
        Field(cut, "Caption").GetAttribute("value").ShouldBe("On a mat");
        Library.Requests.ShouldNotContain(request => request.Locale == "fr");

        Field(cut, "Locale").Change("fr");
        cut.WaitForAssertion(() => cut.FindComponent<CmsDialog>().Instance.Open.ShouldBeTrue());
        Button(cut, "Switch without saving").Click();

        cut.WaitForAssertion(() => Field(cut, "Caption").GetAttribute("value").ShouldBeNull());
        Field(cut, "Title").GetAttribute("value").ShouldBe("Our cat");
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
