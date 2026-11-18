using Bunit;
using FluentCMS.Admin.Components;
using Microsoft.AspNetCore.Components.Forms;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.Media.Admin.Tests;

public sealed class MediaUploadTests : MediaTestContext
{
    [Fact]
    public void TenFilesUploadOneRequestEachAndARefusedOneIsNamedWithItsReasonWhileTheOthersFinish()
    {
        var folder = Library.AddFolder("Photos");
        var uploaded = -1;
        var cut = Render<MediaUpload>(parameters => parameters
            .Add(upload => upload.Folder, folder)
            .Add(upload => upload.OnUploaded, count => uploaded = count));

        InputFileContent[] files =
        [
            .. Enumerable.Range(1, 4).Select(index => InputFileContent.CreateFromText("png", $"photo-{index}.png")),
            InputFileContent.CreateFromText("text", "notes.txt"),
            .. Enumerable.Range(5, 5).Select(index => InputFileContent.CreateFromText("png", $"photo-{index}.png")),
        ];
        cut.FindComponent<InputFile>().UploadFiles(files);

        cut.WaitForAssertion(() => uploaded.ShouldBe(9));
        Library.Requests.Count(request => request.Path == "media").ShouldBe(10);
        Library.Assets.Count.ShouldBe(9);
        Library.Assets.ShouldAllBe(asset => (Guid?)asset["folderId"] == folder);

        var items = cut.FindAll(".media-upload-item");
        items.Count.ShouldBe(10);
        items.Count(item => item.ClassList.Contains("media-upload-done")).ShouldBe(9);
        var refused = items.Single(item => item.ClassList.Contains("media-upload-refused"));
        refused.QuerySelector(".media-upload-name")!.TextContent.ShouldBe("notes.txt");
        refused.QuerySelector(".media-upload-state")!.TextContent.ShouldBe("Refused");
        refused.QuerySelector(".media-upload-reason")!.TextContent.ShouldBe("Its type is not allowed in this site.");
        Announcer.Messages.ShouldBe(["9 uploaded, 1 refused."]);
    }

    [Fact]
    public void EachRefusalOfTheServerIsShownByItsReason()
    {
        var cut = Render<MediaUpload>();

        cut.FindComponent<InputFile>().UploadFiles(
            InputFileContent.CreateFromText("MZ", "setup.exe"),
            InputFileContent.CreateFromText("<svg/>", "drawing.svg"),
            InputFileContent.CreateFromText("png", "fine.png"));

        cut.WaitForAssertion(() => cut.FindAll(".media-upload-reason").Select(reason => reason.TextContent).ShouldBe(
            ["It is a program, which is never stored.", "It is an SVG, which this site refuses."]));
        cut.FindAll(".media-upload-done .media-upload-name").Single().TextContent.ShouldBe("fine.png");
        Library.Assets.ShouldHaveSingleItem()["folderId"].ShouldBeNull();
    }

    [Fact]
    public void FinishedFilesAreClearedOnRequest()
    {
        var cut = Render<MediaUpload>();
        cut.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText("png", "fine.png"));
        cut.WaitForAssertion(() => cut.FindAll(".media-upload-done").Count.ShouldBe(1));
        Announcer.Messages.ShouldBe(["1 uploaded."]);

        Button(cut, "Clear finished").Click();
        cut.FindAll(".media-upload-item").ShouldBeEmpty();
    }

    [Fact]
    public async Task AFileOverTheLimitOfARequestIsRefusedUnread()
    {
        var queue = new MediaUploadQueue(new Api.MediaApi(Library));
        var file = new FakeFile("huge.mov", MediaUploadQueue.MaxFileBytes + 1);

        (await queue.AddAsync([file], null, () => Task.CompletedTask, Token)).ShouldBeTrue();

        queue.Items.Single().State.ShouldBe(MediaUploadState.Refused);
        queue.Items.Single().Code.ShouldBe("FCMS1582");
        file.Opened.ShouldBeFalse();
        Library.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task OnlyTheFileBeingSentIsReadSoTheBytesHeldAreOneFilesAtMost()
    {
        FakeFile[] files = [.. Enumerable.Range(1, 5).Select(index => new FakeFile($"video-{index}.mp4", 4096 * index))];
        List<int> openedAtEachUpload = [];
        Library.BeforeAnswer = request =>
        {
            if (request.Path == "media")
            {
                openedAtEachUpload.Add(files.Count(file => file.Opened));
                request.Content!.Bytes.Length.ShouldBeLessThan(files.Max(file => (int)file.Size) + 1024);
            }

            return Task.CompletedTask;
        };
        var queue = new MediaUploadQueue(new Api.MediaApi(Library));

        await queue.AddAsync(files, null, () => Task.CompletedTask, Token);

        // Each file is opened right before its upload, after the one before has been sent.
        openedAtEachUpload.ShouldBe([1, 2, 3, 4, 5]);
        files.ShouldAllBe(file => file.Disposed);
        Library.Uploads.Select(upload => upload.Bytes.Length).ShouldBe(files.Select(file => (int)file.Size));
        Library.Uploads.ShouldAllBe(upload => upload.Bytes.All(c => c == 'x'));
    }

    [Fact]
    public async Task ASecondBatchArrivingDuringTheFirstJoinsItsUploadsAndTheFirstReportsForBoth()
    {
        var release = new TaskCompletionSource();
        Library.BeforeAnswer = request => request.Path == "media" ? release.Task : Task.CompletedTask;
        var queue = new MediaUploadQueue(new Api.MediaApi(Library));

        var first = queue.AddAsync([new FakeFile("a.png", 10), new FakeFile("b.png", 10)], null, () => Task.CompletedTask, Token);
        await WaitUntilAsync(() => queue.Items.Any(item => item.State == MediaUploadState.Uploading));
        queue.HasUnread.ShouldBeTrue();
        var second = await queue.AddAsync([new FakeFile("c.png", 10)], null, () => Task.CompletedTask, Token);
        release.SetResult();

        second.ShouldBeFalse();
        (await first).ShouldBeTrue();
        Library.Uploads.Select(upload => upload.FileName).ShouldBe(["a.png", "b.png", "c.png"]);
        queue.Items.ShouldAllBe(item => item.State == MediaUploadState.Done);
        queue.HasUnread.ShouldBeFalse();
    }

    [Fact]
    public void AFileTheBrowserCannotReadIsRefusedWithATextOfTheResources()
    {
        var cut = Render<MediaUpload>();

        cut.InvokeAsync(() => cut.Instance.AddAsync([new FakeFile("gone.png", 10) { Fails = true }, new FakeFile("fine.png", 10)]));

        cut.WaitForAssertion(() => cut.FindAll(".media-upload-done").Count.ShouldBe(1));
        cut.Find(".media-upload-refused .media-upload-reason").TextContent.ShouldBe("The file could not be read or uploaded. Try again.");
        cut.Markup.ShouldNotContain("The browser could not read");
    }

    [Fact]
    public void TheUploadTakesNoOtherChoiceWhileFilesOfOneWaitToBeRead()
    {
        var release = new TaskCompletionSource();
        Library.BeforeAnswer = request => request.Path == "media" ? release.Task : Task.CompletedTask;
        var cut = Render<MediaUpload>();

        cut.InvokeAsync(() => cut.Instance.AddAsync([new FakeFile("a.png", 10), new FakeFile("b.png", 10)]));

        cut.WaitForAssertion(() => cut.FindComponent<CmsFileDropZone>().Instance.Disabled.ShouldBeTrue());
        cut.Markup.ShouldContain("Uploading the files chosen.");
        release.SetResult();
        cut.WaitForAssertion(() => cut.FindComponent<CmsFileDropZone>().Instance.Disabled.ShouldBeFalse());
        cut.FindAll(".media-upload-done").Count.ShouldBe(2);
    }

    private static CancellationToken Token => Xunit.TestContext.Current.CancellationToken;

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; !condition(); attempt++)
        {
            attempt.ShouldBeLessThan(500);
            await Task.Delay(10, Token);
        }
    }

    // A file of the browser whose bytes are 'x', which says whether it was opened and its stream disposed.
    private sealed class FakeFile(string name, long size) : IBrowserFile
    {
        public bool Fails { get; init; }

        public bool Opened { get; private set; }

        public bool Disposed { get; private set; }

        public string Name => name;

        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;

        public long Size => size;

        public string ContentType => "application/octet-stream";

        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default)
        {
            if (Fails)
            {
                throw new InvalidOperationException("The browser could not read the file.");
            }

            size.ShouldBeLessThanOrEqualTo(maxAllowedSize);
            Opened = true;
            return new TrackedStream(Enumerable.Repeat((byte)'x', (int)size).ToArray(), () => Disposed = true);
        }
    }

    private sealed class TrackedStream(byte[] bytes, Action disposed) : MemoryStream(bytes)
    {
        protected override void Dispose(bool disposing)
        {
            disposed();
            base.Dispose(disposing);
        }
    }
}
