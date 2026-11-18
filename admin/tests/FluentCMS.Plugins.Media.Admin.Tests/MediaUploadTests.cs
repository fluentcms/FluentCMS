using Bunit;
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
        var file = new HugeFile();

        (await queue.AddAsync([file], null, () => Task.CompletedTask, Xunit.TestContext.Current.CancellationToken)).ShouldBeTrue();

        queue.Items.Single().State.ShouldBe(MediaUploadState.Refused);
        queue.Items.Single().Code.ShouldBe("FCMS1582");
        file.Opened.ShouldBeFalse();
        Library.Requests.ShouldBeEmpty();
    }

    private sealed class HugeFile : IBrowserFile
    {
        public bool Opened { get; private set; }

        public string Name => "huge.mov";

        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;

        public long Size => MediaUploadQueue.MaxFileBytes + 1;

        public string ContentType => "video/quicktime";

        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default)
        {
            Opened = true;
            throw new IOException("Too large.");
        }
    }
}
