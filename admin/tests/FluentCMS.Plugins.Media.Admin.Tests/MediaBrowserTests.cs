using Bunit;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Components;
using FluentCMS.Plugins.Media.Admin.Api;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.Media.Admin.Tests;

public sealed class MediaBrowserTests : MediaTestContext
{
    private MediaBrowseState? changed;

    [Fact]
    public void TheRootShowsTheAssetsOutsideEveryFolderAsTilesWithTheirThumbnails()
    {
        var photos = Library.AddFolder("Photos");
        Library.AddAsset("logo.png", title: "Logo");
        Library.AddAsset("guide.pdf", mimeType: "application/pdf");
        Library.AddAsset("beach.jpg", photos, "image/jpeg");

        var cut = Browse(MediaBrowseState.Default);

        cut.WaitForAssertion(() => Tiles(cut).ShouldBe(["Logo", "guide.pdf"]));
        Library.Requests.Last(request => request.Path.StartsWith("media?", StringComparison.Ordinal)).Path.ShouldBe("media?folder=root&page=1&pageSize=48");
        cut.Find(".media-tile img").GetAttribute("src")!.ShouldStartWith("https://cms.example.com/media/");
        cut.Find(".media-tile img").GetAttribute("alt").ShouldBe("");
        cut.FindAll(".media-tile")[1].TextContent.ShouldContain("PDF");
        cut.Markup.ShouldContain("2 files");
    }

    [Fact]
    public async Task TheTreeListsTheFoldersUnderTheRootAndChoosingOneOpensIt()
    {
        var photos = Library.AddFolder("Photos");
        var summer = Library.AddFolder("Summer", photos);
        Library.AddAsset("beach.jpg", summer, "image/jpeg");

        var cut = Browse(MediaBrowseState.Default);
        cut.WaitForAssertion(() => cut.FindComponent<CmsTree>().Instance.Nodes.Single().Children!.Count.ShouldBe(1));
        var tree = cut.FindComponent<CmsTree>();
        var root = tree.Instance.Nodes.Single();
        root.Text.ShouldBe("All media");
        root.Expanded.ShouldBeTrue();
        root.Children!.Single().Text.ShouldBe("Photos");
        root.Children!.Single().Children!.Single().Text.ShouldBe("Summer");

        await cut.InvokeAsync(() => tree.Instance.SelectedIdChanged.InvokeAsync(summer.ToString("D")));
        cut.WaitForAssertion(() => changed.ShouldBe(MediaBrowseState.Default with { Folder = summer }));

        var opened = Browse(MediaBrowseState.Default with { Folder = summer });
        opened.WaitForAssertion(() => Tiles(opened).ShouldBe(["beach.jpg"]));
        opened.FindComponent<CmsTree>().Instance.SelectedId.ShouldBe(summer.ToString("D"));
        opened.FindComponent<CmsTree>().Instance.Nodes.Single().Children!.Single().Expanded.ShouldBeTrue();
    }

    [Fact]
    public void TheListViewShowsARowPerAssetWithItsTypeSizeAndDimensions()
    {
        Library.AddAsset("logo.png");

        var grid = Browse(MediaBrowseState.Default);
        grid.WaitForAssertion(() => Tiles(grid).Count.ShouldBe(1));
        Button(grid, "List view").Click();
        grid.WaitForAssertion(() => changed.ShouldBe(MediaBrowseState.Default with { View = MediaView.List }));

        var list = Browse(MediaBrowseState.Default with { View = MediaView.List });
        list.WaitForAssertion(() => list.Find(".cms-data-grid tbody").TextContent.ShouldContain("logo.png"));
        var cells = list.FindAll(".cms-data-grid tbody td").Select(cell => cell.TextContent.Trim()).ToList();
        cells.ShouldContain("logo.png");
        cells.ShouldContain("image/png");
        cells.ShouldContain("2 KB");
        cells.ShouldContain("640 × 480");
        HasButton(list, "Grid view").ShouldBeTrue();
    }

    [Fact]
    public void SearchTagAndTypeChangeTheStateFromTheFirstPageAndAFilteredReadTakesInTheSubfolders()
    {
        var photos = Library.AddFolder("Photos");
        var cut = Browse(MediaBrowseState.Default with { Folder = photos, Page = 3 });
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("This folder holds no files."));

        Field(cut, "Search").Input("beach");
        Field(cut, "Tag").Input(" summer ");
        Button(cut, "Search").Click();
        cut.WaitForAssertion(() => changed.ShouldBe(MediaBrowseState.Default with { Folder = photos, Search = "beach", Tag = "summer" }));

        Field(cut, "Type").Change("image/*");
        cut.WaitForAssertion(() => changed.ShouldBe(MediaBrowseState.Default with { Folder = photos, Type = "image/*" }));

        new MediaListQuery { Folder = photos, Search = "beach", Type = "image/*", Tag = "a b" }.ToQueryString()
            .ShouldBe($"?folder={photos:D}&subfolders=true&search=beach&type=image%2F%2A&tag=a%20b&page=1&pageSize=48");
        new MediaListQuery { Search = "beach" }.ToQueryString().ShouldBe("?search=beach&page=1&pageSize=48");
    }

    [Fact]
    public void AcceptHoldsEveryReadToTheTypesTheCallerTakesAndTheFilterCannotBeCleared()
    {
        Library.AddAsset("beach.jpg", mimeType: "image/jpeg");
        Library.AddAsset("clip.mp4", mimeType: "video/mp4");
        Library.AddAsset("guide.pdf", mimeType: "application/pdf");

        var cut = Render<MediaBrowser>(parameters => parameters
            .Add(browser => browser.State, MediaBrowseState.Default with { Type = "application/pdf" })
            .Add(browser => browser.Accept, ["image/*", "video/*"])
            .Add(browser => browser.StateChanged, next => changed = next));

        cut.WaitForAssertion(() => Tiles(cut).ShouldBe(["beach.jpg"]));
        Library.Requests.Last(request => request.Path.StartsWith("media?", StringComparison.Ordinal)).Path.ShouldBe("media?folder=root&type=image%2F%2A&page=1&pageSize=48");
        var select = cut.FindComponents<CmsSelect>().Single(input => input.Instance.Label == "Type").Instance;
        select.Options.Select(option => option.Text).ShouldBe(["Images", "Videos"]);
        select.Clearable.ShouldBeFalse();
        select.Value.ShouldBe("image/*");
        cut.FindAll("select option[value='']").ShouldBeEmpty();

        cut.Render(parameters => parameters.Add(browser => browser.State, MediaBrowseState.Default with { Type = "video/*" }));
        cut.WaitForAssertion(() => Tiles(cut).ShouldBe(["clip.mp4"]));
    }

    [Fact]
    public async Task AnAnswerThatALaterReadOvertookIsDropped()
    {
        var first = Library.AddFolder("First");
        var second = Library.AddFolder("Second");
        Library.AddAsset("old.png", first);
        Library.AddAsset("new.png", second);
        var held = new TaskCompletionSource();
        Library.BeforeAnswer = request => request.Path.Contains(first.ToString("D"), StringComparison.Ordinal) ? held.Task : Task.CompletedTask;

        var cut = Browse(MediaBrowseState.Default with { Folder = first });
        cut.Render(parameters => parameters.Add(browser => browser.State, MediaBrowseState.Default with { Folder = second }));
        cut.WaitForAssertion(() => Tiles(cut).ShouldBe(["new.png"]));

        held.SetResult();
        await Task.Delay(50, Xunit.TestContext.Current.CancellationToken);
        cut.WaitForAssertion(() => Tiles(cut).ShouldBe(["new.png"]));
    }

    [Fact]
    public void AfterASiteSwitchAFolderOfTheOtherSiteGivesWayToTheRoot()
    {
        var old = Library.AddFolder("Old site");
        var cut = Browse(MediaBrowseState.Default with { Folder = old });
        cut.WaitForAssertion(() => cut.FindComponent<CmsTree>().Instance.Nodes.Single().Children!.Count.ShouldBe(1));

        Library.Folders.Clear();
        Admin.Site = new FluentCMS.Admin.Api.MySiteInfo { Id = Guid.NewGuid(), Name = "Shop", Slug = "shop", DefaultLocale = "en", IsDefault = false, Locales = [] };
        cut.Render(parameters => parameters.Add(browser => browser.State, MediaBrowseState.Default with { Folder = old }));

        cut.WaitForAssertion(() => changed.ShouldBe(MediaBrowseState.Default));
        cut.Markup.ShouldNotContain("The folder no longer exists.");
    }

    [Fact]
    public void APagePastTheEndAsksForTheLastPageWithoutShowingAnEmptyFolder()
    {
        Library.AddAsset("logo.png");

        var cut = Browse(MediaBrowseState.Default with { Page = 9 });

        cut.WaitForAssertion(() => changed.ShouldBe(MediaBrowseState.Default));
        cut.Markup.ShouldNotContain("This folder holds no files.");
        cut.Markup.ShouldContain("Loading files");
    }

    [Fact]
    public void AFilteredReadAnnouncesTheNumberItFinds()
    {
        Library.AddAsset("beach.jpg", mimeType: "image/jpeg");
        Library.AddAsset("guide.pdf", mimeType: "application/pdf");

        var cut = Browse(MediaBrowseState.Default);
        cut.WaitForAssertion(() => Tiles(cut).Count.ShouldBe(2));
        Announcer.Messages.ShouldBeEmpty();

        cut.Render(parameters => parameters.Add(browser => browser.State, MediaBrowseState.Default with { Type = "image/*" }));
        cut.WaitForAssertion(() => Tiles(cut).ShouldBe(["beach.jpg"]));
        Announcer.Messages.ShouldBe(["1 file"]);
    }

    [Fact]
    public void PagesFollowTheTotalAndNextAsksForTheNextPage()
    {
        for (var index = 0; index < 50; index++)
        {
            Library.AddAsset($"image-{index:00}.png");
        }

        var cut = Browse(MediaBrowseState.Default);
        cut.WaitForAssertion(() => Tiles(cut).Count.ShouldBe(48));
        cut.Markup.ShouldContain("Page 1 of 2");
        cut.Markup.ShouldContain("50 files");
        Button(cut, "Previous").HasAttribute("disabled").ShouldBeTrue();

        Button(cut, "Next").Click();
        cut.WaitForAssertion(() => changed.ShouldBe(MediaBrowseState.Default with { Page = 2 }));

        var second = Browse(MediaBrowseState.Default with { Page = 2 });
        second.WaitForAssertion(() => Tiles(second).Count.ShouldBe(2));
        second.Markup.ShouldContain("Page 2 of 2");
    }

    [Fact]
    public void ATileIsOpenedByItsButtonAndSelectedByItsCheckboxInTheModeTheCallerChose()
    {
        var logo = Library.AddAsset("logo.png");
        Library.AddAsset("guide.pdf", mimeType: "application/pdf");
        MediaAssetInfo? opened = null;
        IReadOnlyList<MediaAssetInfo> selected = [];

        var cut = Render<MediaBrowser>(parameters => parameters
            .Add(browser => browser.State, MediaBrowseState.Default)
            .Add(browser => browser.SelectionMode, CmsSelectionMode.One)
            .Add(browser => browser.SelectedChanged, items => selected = items)
            .Add(browser => browser.OnOpen, asset => opened = asset));
        cut.WaitForAssertion(() => Tiles(cut).Count.ShouldBe(2));

        cut.FindAll(".media-tile-open")[0].Click();
        cut.WaitForAssertion(() => opened.ShouldNotBeNull().Id.ShouldBe(logo));

        cut.Find("input[aria-label='Select logo.png']").Change(true);
        cut.WaitForAssertion(() => selected.Select(item => item.FileName).ShouldBe(["logo.png"]));
        cut.Render(parameters => parameters.Add(browser => browser.Selected, selected));
        cut.Find("input[aria-label='Select guide.pdf']").Change(true);
        cut.WaitForAssertion(() => selected.Select(item => item.FileName).ShouldBe(["guide.pdf"]));

        var none = Browse(MediaBrowseState.Default);
        none.WaitForAssertion(() => Tiles(none).Count.ShouldBe(2));
        none.FindAll(".media-tile-check").ShouldBeEmpty();
    }

    [Fact]
    public void TheListShowsTheSelectionByIdAfterTheAssetsAreReadAgain()
    {
        var logo = Library.AddAsset("logo.png", tags: "brand");
        var cut = Render<MediaBrowser>(parameters => parameters
            .Add(browser => browser.State, MediaBrowseState.Default with { View = MediaView.List })
            .Add(browser => browser.SelectionMode, CmsSelectionMode.Many));
        cut.WaitForAssertion(() => cut.Find(".cms-data-grid tbody").TextContent.ShouldContain("logo.png"));

        // An earlier read of the same asset: equal by its id, not by value, since its tags are another list.
        var earlier = cut.FindComponent<CmsDataGrid<MediaAssetInfo>>().Instance.Items.Single() with { Tags = ["brand"] };
        cut.Render(parameters => parameters.Add(browser => browser.Selected, [earlier]));

        var grid = cut.FindComponent<CmsDataGrid<MediaAssetInfo>>().Instance;
        grid.SelectedItems.Single().ShouldBeSameAs(grid.Items.Single());
        grid.Items.Single().Id.ShouldBe(logo);
    }

    [Fact]
    public async Task TheListKeepsWhatIsSelectedOnOtherPagesWhenItsRowsAreSelected()
    {
        var shown = Library.AddAsset("shown.png");
        var elsewhere = new MediaAssetInfo { Id = Guid.NewGuid(), FileName = "elsewhere.png", MimeType = "image/png", Size = 1, Tags = [], Urls = new MediaAssetUrls { Original = "/o" } };
        IReadOnlyList<MediaAssetInfo>? selected = null;
        var cut = Render<MediaBrowser>(parameters => parameters
            .Add(browser => browser.State, MediaBrowseState.Default with { View = MediaView.List })
            .Add(browser => browser.SelectionMode, CmsSelectionMode.Many)
            .Add(browser => browser.Selected, [elsewhere])
            .Add(browser => browser.SelectedChanged, items => selected = items));
        cut.WaitForAssertion(() => cut.Find(".cms-data-grid tbody").TextContent.ShouldContain("shown.png"));

        var grid = cut.FindComponent<CmsDataGrid<MediaAssetInfo>>();
        await cut.InvokeAsync(() => grid.Instance.SelectedItemsChanged.InvokeAsync([grid.Instance.Items.Single()]));

        selected.ShouldNotBeNull().Select(item => item.Id).ShouldBe([elsewhere.Id, shown]);
    }

    [Fact]
    public void ARefusedReadSaysWhyAndAMissingFolderLeadsBackToTheRoot()
    {
        Library.ListProblem = new ApiProblem { Status = 403, Code = "FCMS1100" };
        var refused = Browse(MediaBrowseState.Default);
        refused.WaitForAssertion(() => refused.Find("[role=alert]").TextContent.ShouldContain("You may not see the files of this folder."));
        HasButton(refused, "Try again").ShouldBeFalse();

        Library.ListProblem = new ApiProblem { Status = 404, Code = "FCMS1586" };
        var missing = Browse(MediaBrowseState.Default with { Folder = Guid.NewGuid() });
        missing.WaitForAssertion(() => missing.Find("[role=alert]").TextContent.ShouldContain("The folder no longer exists."));
        Button(missing, "Go to all folders").Click();
        missing.WaitForAssertion(() => changed.ShouldBe(MediaBrowseState.Default));
    }

    private IRenderedComponent<MediaBrowser> Browse(MediaBrowseState state) =>
        Render<MediaBrowser>(parameters => parameters
            .Add(browser => browser.State, state)
            .Add(browser => browser.StateChanged, next => changed = next));

    private static List<string> Tiles(IRenderedComponent<MediaBrowser> cut) =>
        [.. cut.FindAll(".media-tile-name").Select(name => name.TextContent.Trim())];
}
