using Bunit;
using FluentCMS.Admin.Components;
using FluentCMS.Admin.Pickers;
using Microsoft.AspNetCore.Components.Forms;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.Media.Admin.Tests;

public sealed class MediaPickerTests : MediaTestContext
{
    private static CancellationToken Token => Xunit.TestContext.Current.CancellationToken;

    [Fact]
    public async Task ActivatingAnAssetChoosesItAndThePickerAnswersOnceTheDialogHasClosed()
    {
        Library.AddAsset("logo.png", title: "Logo");
        var beach = Library.AddAsset("beach.jpg", mimeType: "image/jpeg");

        var (picked, host) = Open(new MediaPickerRequest());
        host.WaitForAssertion(() => host.FindAll(".media-tile").Count.ShouldBe(2));
        host.Find("fluent-dialog").GetAttribute("aria-label").ShouldBe("Choose an asset");

        host.FindAll(".media-tile-open")[1].Click();
        picked.IsCompleted.ShouldBeFalse();
        await CloseAsync(host);

        var item = (await picked).ShouldNotBeNull().ShouldHaveSingleItem();
        item.Id.ShouldBe(beach);
        item.FileName.ShouldBe("beach.jpg");
        item.MimeType.ShouldBe("image/jpeg");
        item.ThumbnailUrl.ShouldBe($"/media/{beach:D}/thumbnail/beach.jpg");
        Overlays.Shown.ShouldBeEmpty();
    }

    [Fact]
    public async Task APickerOfSeveralStartsWithTheChosenAssetsAndKeepsTheOrderTheyWereChosenIn()
    {
        var first = Library.AddAsset("a.png");
        var second = Library.AddAsset("b.png");
        var third = Library.AddAsset("c.png");

        var (picked, host) = Open(new MediaPickerRequest { Multiple = true, Selected = [second, first], Locale = "fr" });
        host.WaitForAssertion(() => host.Markup.ShouldContain("2 assets chosen"));
        host.Find("fluent-dialog").GetAttribute("aria-label").ShouldBe("Choose assets");
        host.WaitForAssertion(() => host.FindAll(".media-tile-check").Count.ShouldBe(3));
        Library.Requests.Where(request => request.Path == $"media/{second:D}").ShouldHaveSingleItem().Locale.ShouldBe("fr");
        host.FindAll(".media-tile-check")[0].Change(false);
        host.FindAll(".media-tile-check")[2].Change(true);
        host.FindAll(".media-tile-check")[0].Change(true);
        Button(host, "Choose").Click();
        await CloseAsync(host);

        (await picked).ShouldNotBeNull().Select(item => item.Id).ShouldBe([second, third, first]);
    }

    [Fact]
    public async Task AnAssetChosenBeforeThatCannotBeReadStaysChosenWithItsIdOnly()
    {
        var gone = Guid.NewGuid();
        var first = Library.AddAsset("a.png");
        var second = Library.AddAsset("b.png");

        var (picked, host) = Open(new MediaPickerRequest { Multiple = true, Selected = [gone, first] });
        host.WaitForAssertion(() => host.FindAll(".media-tile-check").Count.ShouldBe(2));
        host.Markup.ShouldContain("2 assets chosen");
        host.FindAll(".media-tile-check")[1].Change(true);
        Button(host, "Choose").Click();
        await CloseAsync(host);

        var items = (await picked).ShouldNotBeNull();
        items.Select(item => item.Id).ShouldBe([gone, first, second]);
        items[0].FileName.ShouldBeEmpty();
    }

    [Fact]
    public async Task AChoiceMadeWhileTheAssetsChosenBeforeAreReadIsKept()
    {
        var before = Library.AddAsset("a.png");
        var ticked = Library.AddAsset("b.png");
        var held = new TaskCompletionSource();
        Library.BeforeAnswer = request => request.Path == $"media/{before:D}" ? held.Task : Task.CompletedTask;

        var (picked, host) = Open(new MediaPickerRequest { Multiple = true, Selected = [before] });
        host.WaitForAssertion(() => host.FindAll(".media-tile-check").Count.ShouldBe(2));
        host.FindAll(".media-tile-check")[1].Change(true);
        held.SetResult();
        host.WaitForAssertion(() => host.Markup.ShouldContain("2 assets chosen"));

        Button(host, "Choose").Click();
        await CloseAsync(host);
        (await picked).ShouldNotBeNull().Select(item => item.Id).ShouldBe([before, ticked]);
    }

    [Fact]
    public void AReadInTheRequestsLocaleThatFailsKeepsThePickerOpen()
    {
        var logo = Library.AddAsset("logo.png");
        var (picked, host) = Open(new MediaPickerRequest { Locale = "fr" });
        host.WaitForAssertion(() => host.FindAll(".media-tile-check").Count.ShouldBe(1));
        host.FindAll(".media-tile-check")[0].Change(true);
        Library.BeforeAnswer = request => request.Path == $"media/{logo:D}" ? throw new HttpRequestException("offline") : Task.CompletedTask;

        Button(host, "Choose").Click();

        host.WaitForAssertion(() => host.Find("[role=alert]").TextContent.ShouldBe("The chosen assets could not be read. Try again."));
        host.FindComponent<CmsDialog>().Instance.Open.ShouldBeTrue();
        picked.IsCompleted.ShouldBeFalse();
    }

    [Fact]
    public async Task ClosingWithoutAChoiceAnswersNull()
    {
        Library.AddAsset("logo.png");
        var (picked, host) = Open(new MediaPickerRequest());
        host.WaitForAssertion(() => host.FindAll(".media-tile").Count.ShouldBe(1));

        Button(host, "Cancel").Click();
        await CloseAsync(host);

        (await picked).ShouldBeNull();
        Overlays.Shown.ShouldBeEmpty();
    }

    [Fact]
    public async Task CancellingTheTokenClosesThePickerWithoutAChoice()
    {
        using var cancel = new CancellationTokenSource();
        var picked = new MediaPicker(Overlays).PickAsync(new MediaPickerRequest(), cancel.Token);
        Overlays.Shown.ShouldHaveSingleItem();

        await cancel.CancelAsync();

        (await picked).ShouldBeNull();
        Overlays.Shown.ShouldBeEmpty();
    }

    [Fact]
    public async Task APickerOfImagesShowsNoPdfAndItsUploadRefusesOneBeforeSendingIt()
    {
        Admin.Grant(MediaPermissions.Upload);
        Library.AddAsset("logo.png");
        Library.AddAsset("guide.pdf", mimeType: "application/pdf");

        var (picked, host) = Open(new MediaPickerRequest { Accept = ["image/*"] });
        host.WaitForAssertion(() => host.FindAll(".media-tile").Count.ShouldBe(1));
        host.Find(".media-tile-name").TextContent.ShouldBe("logo.png");
        Library.Requests.Last(request => request.Path.StartsWith("media?", StringComparison.Ordinal)).Path.ShouldContain("type=image%2F%2A");

        host.FindComponent<CmsFileDropZone>().Instance.Accept.ShouldBe("image/*");
        host.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromText("%PDF", "manual.pdf", contentType: "application/pdf"),
            InputFileContent.CreateFromText("png", "photo.png", contentType: "image/png"));

        host.WaitForAssertion(() => host.FindAll(".media-upload-done").Count.ShouldBe(1));
        host.Find(".media-upload-refused .media-upload-name").TextContent.ShouldBe("manual.pdf");
        host.Find(".media-upload-refused .media-upload-reason").TextContent.ShouldBe("Its type is not one this field takes.");
        Library.Uploads.Select(upload => upload.FileName).ShouldBe(["photo.png"]);
        host.WaitForAssertion(() => host.FindAll(".media-tile").Count.ShouldBe(2));

        Button(host, "Cancel").Click();
        await CloseAsync(host);
        (await picked).ShouldBeNull();
    }

    [Fact]
    public void TheUploadShowsOnlyForACallerWhoMayUpload()
    {
        Library.AddAsset("logo.png");
        var (_, host) = Open(new MediaPickerRequest());
        host.WaitForAssertion(() => host.FindAll(".media-tile").Count.ShouldBe(1));
        host.FindComponents<MediaUpload>().ShouldBeEmpty();

        Admin.Grant(MediaPermissions.Upload);
        var (_, allowed) = Open(new MediaPickerRequest());
        allowed.WaitForAssertion(() => allowed.FindComponents<MediaUpload>().ShouldHaveSingleItem());
    }

    private (Task<IReadOnlyList<MediaPickerItem>?> Picked, IRenderedComponent<Microsoft.AspNetCore.Components.IComponent> Host) Open(MediaPickerRequest request)
    {
        Overlays.Shown.Clear();
        var picked = new MediaPicker(Overlays).PickAsync(request, Token);
        return (picked, Render(Overlays.Host));
    }

    // The dialog's close, which the browser reports once the dialog has hidden and given focus back.
    private static Task CloseAsync(IRenderedComponent<Microsoft.AspNetCore.Components.IComponent> host)
    {
        var dialog = host.FindComponent<CmsDialog>();
        host.WaitForAssertion(() => dialog.Instance.Open.ShouldBeFalse());
        return host.InvokeAsync(() => dialog.Instance.OnClosed.InvokeAsync());
    }
}
