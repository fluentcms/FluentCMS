using AngleSharp.Dom;
using Bunit;
using FluentCMS.Admin.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.Media.Admin.Tests;

public sealed class MediaPageTests : MediaTestContext
{
    [Fact]
    public void AReaderBrowsesButIsOfferedNoUploadNoSelectionAndNoFolderAction()
    {
        Library.AddFolder("Photos");
        Library.AddAsset("logo.png");

        var cut = Render<MediaPage>();

        cut.WaitForAssertion(() => cut.FindAll(".media-tile").Count.ShouldBe(1));
        cut.Find("h1").TextContent.ShouldBe("Media");
        cut.FindAll(".media-upload").ShouldBeEmpty();
        cut.FindAll(".media-tile-check").ShouldBeEmpty();
        cut.FindAll(".media-folder-actions").ShouldBeEmpty();
        JSInterop.Invocations.ShouldNotContain(invocation => invocation.Identifier == "import");
    }

    [Fact]
    public void EachActionShowsForThePermissionThatAllowsIt()
    {
        Admin.Grant(MediaPermissions.Upload, MediaPermissions.Update, MediaPermissions.ManageFolders);
        var photos = Library.AddFolder("Photos");
        Library.AddAsset("logo.png", photos);

        var cut = RenderAt($"?folder={photos:D}");

        cut.WaitForAssertion(() => cut.FindAll(".media-tile-check").Count.ShouldBe(1));
        cut.FindAll(".media-upload").Count.ShouldBe(1);
        HasButton(cut, "New folder").ShouldBeTrue();
        HasButton(cut, "Rename folder").ShouldBeTrue();
        HasButton(cut, "Move folder").ShouldBeTrue();
        HasButton(cut, "Delete folder").ShouldBeTrue();
        cut.WaitForAssertion(() => JSInterop.Invocations.ShouldContain(invocation => invocation.Identifier == "import" && (string)invocation.Arguments[0]! == MediaDropZone.ModulePath));
    }

    [Fact]
    public void TheRootOffersANewFolderButNothingToRenameMoveOrDelete()
    {
        Admin.Grant(MediaPermissions.ManageFolders);

        var cut = Render<MediaPage>();

        cut.WaitForAssertion(() => HasButton(cut, "New folder").ShouldBeTrue());
        HasButton(cut, "Rename folder").ShouldBeFalse();
        HasButton(cut, "Delete folder").ShouldBeFalse();
    }

    [Fact]
    public void TheAddressGivesTheStateAndAChangeOfStateWritesIt()
    {
        var photos = Library.AddFolder("Photos");
        Library.AddAsset("beach.jpg", photos, "image/jpeg", tags: "summer");

        var cut = RenderAt($"?folder={photos:D}&view=list&tag=summer&page=abc");

        cut.WaitForAssertion(() => cut.Find(".cms-data-grid tbody").TextContent.ShouldContain("beach.jpg"));
        Library.Requests.Last(request => request.Path.StartsWith("media?", StringComparison.Ordinal)).Path
            .ShouldBe($"media?folder={photos:D}&subfolders=true&tag=summer&page=1&pageSize=48");

        Button(cut, "Grid view").Click();
        var navigation = Services.GetRequiredService<NavigationManager>();
        cut.WaitForAssertion(() => navigation.Uri.ShouldBe($"http://localhost/?folder={photos:D}&tag=summer"));
    }

    [Fact]
    public void AFolderIsCreatedRenamedMovedAndDeletedOnceItIsEmpty()
    {
        Admin.Grant(MediaPermissions.ManageFolders);
        var archive = Library.AddFolder("Archive");
        var cut = Render<MediaPage>();
        cut.WaitForAssertion(() => HasButton(cut, "New folder").ShouldBeTrue());

        Button(cut, "New folder").Click();
        Field(cut, "Folder name").Input("Photos");
        Button(cut, "Create").Click();
        cut.WaitForAssertion(() => Library.Folders.Count.ShouldBe(2));
        var photos = (Guid)Library.Folders.Single(folder => folder["name"]!.GetValue<string>() == "Photos")["id"]!;
        Library.Folders.Single(folder => (Guid)folder["id"]! == photos)["parentId"].ShouldBeNull();
        cut.WaitForAssertion(() => Announcer.Messages.ShouldContain("Folder Photos created."));

        // The same name again is refused in the dialog.
        Button(cut, "New folder").Click();
        Field(cut, "Folder name").Input("Photos");
        Button(cut, "Create").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("A folder of this name is already there."));

        var inPhotos = RenderAt($"?folder={photos:D}");
        inPhotos.WaitForAssertion(() => HasButton(inPhotos, "Rename folder").ShouldBeTrue());
        inPhotos.WaitForAssertion(() => inPhotos.FindComponent<MediaBrowser>().Instance.Folders.Count.ShouldBe(2));
        Button(inPhotos, "Rename folder").Click();
        Field(inPhotos, "Folder name").Input("Pictures");
        Button(inPhotos, "Rename").Click();
        inPhotos.WaitForAssertion(() => Announcer.Messages.ShouldContain("Folder renamed to Pictures."));
        Library.Folders.Single(folder => (Guid)folder["id"]! == photos)["name"]!.GetValue<string>().ShouldBe("Pictures");

        Button(inPhotos, "Move folder").Click();
        inPhotos.WaitForAssertion(() => inPhotos.FindComponents<CmsSelect>().Single(select => select.Instance.Label == "Move to")
            .Instance.Options.Select(option => option.Text).ShouldBe(["Archive"]));
        Field(inPhotos, "Move to").Change(archive.ToString("D"));
        Button(inPhotos, "Move").Click();
        inPhotos.WaitForAssertion(() => ((Guid?)Library.Folders.Single(folder => (Guid)folder["id"]! == photos)["parentId"]).ShouldBe(archive));

        Library.AddAsset("kept.png", photos);
        Button(inPhotos, "Delete folder").Click();
        DeleteButton(inPhotos).Click();
        inPhotos.WaitForAssertion(() => inPhotos.Markup.ShouldContain("The folder holds files or folders. Move or delete them first."));
        Library.Folders.Count.ShouldBe(2);

        Library.Assets.Clear();
        DeleteButton(inPhotos).Click();
        inPhotos.WaitForAssertion(() => Library.Folders.Count.ShouldBe(1));
        inPhotos.WaitForAssertion(() => Services.GetRequiredService<NavigationManager>().Uri.ShouldBe($"http://localhost/?folder={archive:D}"));
        Announcer.Messages.ShouldContain("Folder Pictures deleted.");
    }

    [Fact]
    public void SelectedAssetsMoveToTheFolderChosen()
    {
        Admin.Grant(MediaPermissions.Update);
        var photos = Library.AddFolder("Photos");
        var logo = Library.AddAsset("logo.png");
        var beach = Library.AddAsset("beach.png");
        Library.AddAsset("guide.pdf", mimeType: "application/pdf");

        var cut = Render<MediaPage>();
        cut.WaitForAssertion(() => cut.FindAll(".media-tile-check").Count.ShouldBe(3));
        cut.FindAll("fluent-button").ShouldNotContain(button => button.TextContent.Contains("selected", StringComparison.Ordinal));

        cut.Find("input[aria-label='Select logo.png']").Change(true);
        cut.WaitForAssertion(() => HasButton(cut, "Move 1 selected").ShouldBeTrue());
        cut.Find("input[aria-label='Select beach.png']").Change(true);
        cut.WaitForAssertion(() => HasButton(cut, "Move 2 selected").ShouldBeTrue());
        Button(cut, "Move 2 selected").Click();
        Field(cut, "Move to").Change(photos.ToString("D"));
        Button(cut, "Move").Click();

        cut.WaitForAssertion(() => Library.FolderOf(beach).ShouldBe(photos));
        Library.FolderOf(logo).ShouldBe(photos);
        cut.WaitForAssertion(() => cut.FindAll(".media-tile-name").Select(name => name.TextContent).ShouldBe(["guide.pdf"]));
        Announcer.Messages.ShouldContain("2 moved.");
    }

    [Fact]
    public void AfterAPartlyFailedMoveTheAssetsNotMovedStaySelected()
    {
        Admin.Grant(MediaPermissions.Update);
        var photos = Library.AddFolder("Photos");
        var logo = Library.AddAsset("logo.png");
        var beach = Library.AddAsset("beach.png");
        Library.RefuseMoveOf = beach;

        var cut = Render<MediaPage>();
        cut.WaitForAssertion(() => cut.FindAll(".media-tile-check").Count.ShouldBe(2));
        cut.Find("input[aria-label='Select logo.png']").Change(true);
        cut.WaitForAssertion(() => HasButton(cut, "Move 1 selected").ShouldBeTrue());
        cut.Find("input[aria-label='Select beach.png']").Change(true);
        cut.WaitForAssertion(() => HasButton(cut, "Move 2 selected").ShouldBeTrue());
        Button(cut, "Move 2 selected").Click();
        Field(cut, "Move to").Change(photos.ToString("D"));
        Button(cut, "Move").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("You may not make this change."));
        Library.FolderOf(logo).ShouldBe(photos);
        Library.FolderOf(beach).ShouldBeNull();
        cut.WaitForAssertion(() => HasButton(cut, "Move 1 selected").ShouldBeTrue());
        cut.Find("input[aria-label='Select beach.png']").HasAttribute("checked").ShouldBeTrue();
    }

    [Fact]
    public void OpeningAnAssetShowsItsDetail()
    {
        Library.AddAsset("logo.png", title: "Company logo");
        var cut = Render<MediaPage>();
        cut.WaitForAssertion(() => cut.FindAll(".media-tile-open").Count.ShouldBe(1));

        cut.Find(".media-tile-open").Click();

        cut.WaitForAssertion(() => cut.FindComponent<MediaAssetPanel>().Instance.Open.ShouldBeTrue());
        cut.WaitForAssertion(() => Field(cut, "Title").GetAttribute("value").ShouldBe("Company logo"));
        var facts = cut.Find(".media-panel-facts").TextContent;
        facts.ShouldContain("logo.png");
        facts.ShouldContain("image/png");
        facts.ShouldContain("2 KB");
    }

    private IRenderedComponent<MediaPage> RenderAt(string query)
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo(query);
        return Render<MediaPage>();
    }

    private static IElement DeleteButton(IRenderedComponent<MediaPage> cut) =>
        cut.FindAll("fluent-button").Last(button => button.TextContent.Trim() == "Delete");
}
