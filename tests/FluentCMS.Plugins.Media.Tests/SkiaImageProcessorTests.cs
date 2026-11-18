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

    public static TheoryData<bool, int?, int?, ImageFit> Shrunk => new()
    {
        { false, 100, null, ImageFit.Contain },
        { false, null, 37, ImageFit.Contain },
        { false, 100, 100, ImageFit.Contain },
        { false, 64, 64, ImageFit.Cover },
        { false, 50, 20, ImageFit.Fill },
        { true, 50, null, ImageFit.Contain },
        { true, 60, 60, ImageFit.Cover },
    };

    // A JPEG decodes at an eighth of its size and more, so a result far smaller than the image decodes it smaller; the
    // size, the orientation and what is shown stay those of the whole image.
    [Theory]
    [MemberData(nameof(Shrunk))]
    public async Task AnImageFarLargerThanTheResultIsDecodedSmallerToTheSameSize(bool turned, int? width, int? height, ImageFit fit)
    {
        var processor = new SkiaImageProcessor();
        var bytes = turned ? TurnedJpeg(2003, 1001) : Bands(2003, 1001, SkiaSharp.SKEncodedImageFormat.Jpeg);
        var transform = new ImageTransform { Width = width, Height = height, Fit = fit, MimeType = "image/png" };
        var (shownWidth, shownHeight) = turned ? (1001, 2003) : (2003, 1001);

        await using var source = new MemoryStream(bytes);
        using var output = new MemoryStream();
        await processor.ProcessAsync(source, transform, output, Token);
        using var codec = SkiaSharp.SKCodec.Create(new MemoryStream(bytes));
        var (expectedWidth, expectedHeight) = MediaTransforms.SizeOf(transform, shownWidth, shownHeight);

        SkiaImageProcessor.DecodedSize(codec, (float)expectedWidth / shownWidth).Width.ShouldBeLessThan(2003 / 2);
        using var image = Decode(output.ToArray());
        (image.Width, image.Height).ShouldBe((expectedWidth, expectedHeight));
        if (fit == ImageFit.Cover)
        {
            // The centre of the bands, between green and blue.
            Near(image.GetPixel(turned ? image.Width / 2 : image.Width / 4, turned ? image.Height / 4 : image.Height / 2), Green);
        }
        else if (!turned)
        {
            Near(image.GetPixel(image.Width / 8, image.Height / 2), Red);
            Near(image.GetPixel(image.Width * 7 / 8, image.Height / 2), Yellow);
        }
        else
        {
            // Turned a quarter clockwise, the bands run from top to bottom.
            Near(image.GetPixel(image.Width / 2, image.Height / 8), Red);
            Near(image.GetPixel(image.Width / 2, image.Height * 7 / 8), Yellow);
        }
    }

    // JPEG decodes in eighths and rounds to the nearest, so 0.3 would decode at a quarter; WebP rounds to a pixel.
    [Theory]
    [InlineData(SkiaSharp.SKEncodedImageFormat.Jpeg)]
    [InlineData(SkiaSharp.SKEncodedImageFormat.Webp)]
    public void TheSizeDecodedIsNeverUnderTwiceTheScaleOfTheResult(SkiaSharp.SKEncodedImageFormat format)
    {
        using var codec = SkiaSharp.SKCodec.Create(new MemoryStream(Bands(2003, 1001, format)));

        for (var scale = 0.01f; scale < 0.5f; scale += 0.01f)
        {
            var size = SkiaImageProcessor.DecodedSize(codec, scale);
            size.Width.ShouldBeGreaterThanOrEqualTo((int)Math.Ceiling(2003 * scale * 2.0), $"{scale}");
            size.Height.ShouldBeGreaterThanOrEqualTo((int)Math.Ceiling(1001 * scale * 2.0), $"{scale}");
            size.Width.ShouldBeLessThanOrEqualTo(2003);
        }

        SkiaImageProcessor.DecodedSize(codec, 0.15f).Width.ShouldBeLessThan(2003, "0.3 decodes at three eighths, not whole");
    }

    [Fact]
    public void AResultNotFarSmallerThanTheImageDecodesTheWholeSize()
    {
        using var codec = SkiaSharp.SKCodec.Create(new MemoryStream(Bands(2003, 1001, SkiaSharp.SKEncodedImageFormat.Jpeg)));

        SkiaImageProcessor.DecodedSize(codec, 0.5f).ShouldBe(codec.Info.Size);
        SkiaImageProcessor.DecodedSize(codec, 2f).ShouldBe(codec.Info.Size);
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

    private static void Near(SkiaSharp.SKColor actual, SkiaSharp.SKColor expected) =>
        (Math.Abs(actual.Red - expected.Red) + Math.Abs(actual.Green - expected.Green) + Math.Abs(actual.Blue - expected.Blue)).ShouldBeLessThan(60, $"{actual} is not near {expected}");

    internal sealed class ForwardOnlyStream(byte[] bytes) : Stream
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
