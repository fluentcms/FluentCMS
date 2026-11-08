using FluentCMS.Media;
using Shouldly;
using Xunit;
using static FluentCMS.Plugins.Media.Tests.MediaHost;
using static FluentCMS.Plugins.Media.Tests.Transforms;

namespace FluentCMS.Plugins.Media.Tests;

public sealed class SkiaImageProcessorTests
{
    [Fact]
    public async Task AStreamThatCannotSeekIsIdentifiedAndProcessed()
    {
        var processor = new SkiaImageProcessor();
        var bytes = Bands(400, 200);

        await using var identified = new ForwardOnlyStream(bytes);
        var header = await processor.IdentifyAsync(identified, Token);
        await using var source = new ForwardOnlyStream(bytes);
        using var output = new MemoryStream();
        await processor.ProcessAsync(source, new ImageTransform { Width = 100, Height = 100, Fit = ImageFit.Cover, MimeType = "image/png", FocalPoint = new MediaFocalPoint(0.875, 0.5) }, output, Token);

        header.ShouldBe(new ImageHeader(400, 200, 1));
        using var image = Decode(output.ToArray());
        (image.Width, image.Height).ShouldBe((100, 100));
        image.GetPixel(50, 50).ShouldBe(Yellow);
    }

    [Fact]
    public async Task TheTypesItReadsAndWritesLeaveOutAvifAndGifOutput()
    {
        var processor = new SkiaImageProcessor();
        await using var pdf = new MemoryStream(Files.Pdf());

        ((string[])["image/jpeg", "image/png", "image/gif", "image/webp"]).ShouldAllBe(type => processor.CanDecode(type));
        ((string[])["image/jpeg", "image/png", "image/webp"]).ShouldAllBe(type => processor.CanEncode(type));
        processor.CanEncode("image/avif").ShouldBeFalse();
        processor.CanEncode("image/gif").ShouldBeFalse();
        (await processor.IdentifyAsync(pdf, Token)).ShouldBeNull();
    }

    private sealed class ForwardOnlyStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
