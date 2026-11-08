using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace FluentCMS.Plugins.Media.Tests;

// The pieces an upload goes through: detection by bytes, image headers, the SVG pass and the local storage.
public sealed class MediaFileTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> Signatures => new()
    {
        { "FFD8FFE0", "image/jpeg" },
        { "89504E470D0A1A0A", "image/png" },
        { "474946383961", "image/gif" },
        { "52494646000000005745425056503820", "image/webp" },
        { "524946460000000057415645666D7420", "audio/wav" },
        { "255044462D312E37", "application/pdf" },
        { "0000001C6674797061766966", "image/avif" },
        { "00000018667479706D703432", "video/mp4" },
        { "1A45DFA3A34286810142F7810142F2810442F381084282847765626D", "video/webm" },
        { "4944330400", "audio/mpeg" },
        { "FFFB9064", "audio/mpeg" },
    };

    [Theory]
    [MemberData(nameof(Signatures))]
    public void ATypeIsKnownByItsFirstBytes(string hex, string mimeType) =>
        MediaTypes.Detect(Convert.FromHexString(hex)).ShouldNotBeNull().MimeType.ShouldBe(mimeType);

    [Theory]
    [InlineData("4D5A90")]
    [InlineData("7F454C4602")]
    [InlineData("FEEDFACF")]
    [InlineData("CFFAEDFE")]
    [InlineData("CAFEBABE")]
    [InlineData("2321")]
    [InlineData("EFBBBF2321")]
    public void AnExecutableIsKnownByItsSignature(string hex) => MediaTypes.IsExecutable(Convert.FromHexString(hex)).ShouldBeTrue();

    [Fact]
    public void NeitherAnUnknownSignatureNorAnImageIsAnExecutable()
    {
        MediaTypes.Detect("hello"u8).ShouldBeNull();
        MediaTypes.Detect(Convert.FromHexString("0000001C6674797068656963")).ShouldBeNull();
        MediaTypes.Detect(Convert.FromHexString("1A45DFA3A3428681")).ShouldBeNull();
        MediaTypes.IsExecutable(Files.Png(1, 1)).ShouldBeFalse();
        MediaTypes.HasExecutableName("photo.jpg.exe").ShouldBeTrue();
        MediaTypes.HasExecutableName("photo.JPG").ShouldBeFalse();
    }

    [Fact]
    public async Task AJpegsSizeIsFoundAfterTheSegmentsBeforeItsFrame()
    {
        byte[] exif = [0xFF, 0xE1, 0x01, 0x02, .. new byte[0x100]];
        var plain = Files.Jpeg(1920, 1080);
        byte[] jpeg = [.. plain[..2], .. exif, .. plain[2..]];
        using var stream = new MemoryStream(jpeg);

        (await ImageSize.ReadAsync(stream, MediaTypes.Jpeg, Token)).ShouldBe((1920, 1080));
        (await ImageSize.ReadAsync(new MemoryStream([0xFF, 0xD8, 0xFF, 0xDA]), MediaTypes.Jpeg, Token)).ShouldBeNull();
    }

    [Fact]
    public async Task TheLossyAndLosslessWebPHeadersGiveTheirSize()
    {
        var lossy = new byte[30];
        "RIFF\0\0\0\0WEBPVP8 \0\0\0\0"u8.CopyTo(lossy);
        lossy[26] = 0x80;
        lossy[27] = 0x02;
        lossy[28] = 0xE0;
        lossy[29] = 0x01;
        var lossless = new byte[30];
        "RIFF\0\0\0\0WEBPVP8L\0\0\0\0"u8.CopyTo(lossless);
        lossless[20] = 0x2F;
        var bits = (99u) | (49u << 14);
        BitConverter.GetBytes(bits).CopyTo(lossless, 21);

        (await ImageSize.ReadAsync(new MemoryStream(lossy), MediaTypes.WebP, Token)).ShouldBe((640, 480));
        (await ImageSize.ReadAsync(new MemoryStream(lossless), MediaTypes.WebP, Token)).ShouldBe((100, 50));
    }

    [Fact]
    public void SanitisingKeepsTheDrawingAndDropsWhatRunsOrReachesOut()
    {
        var svg = SvgDocument.Parse(new MemoryStream(Files.Svg("""
            <style>@import url(https://evil.example/x.css);</style>
            <style>rect { fill: red; }</style>
            <rect width="5" height="5" style="fill: url(javascript:alert(1))" onmouseover="alert(1)" OnLoad="alert(2)"/>
            <a href=" java&#x09;script:alert(3)"><text>link</text></a>
            <image href="data:image/svg+xml;base64,PHN2Zz48L3N2Zz4=" width="1" height="1"/>
            <image href="data:image/png;base64,iVBORw0KGgo=" width="1" height="1"/>
            <animate attributeName="href" to="javascript:alert(4)"/>
            <set attributeName="onclick" to="alert(5)"/>
            <animate attributeName="opacity" from="0" to="1" dur="1s"/>
            <html:script xmlns:html="http://www.w3.org/1999/xhtml">alert(6)</html:script>
            <circle r="1" inkscape:label="dot" xmlns:inkscape="http://www.inkscape.org/namespaces/inkscape"/>
            """))).ShouldNotBeNull();

        SvgDocument.Sanitize(svg);
        var text = Encoding.UTF8.GetString(SvgDocument.Save(svg));

        text.ShouldContain("rect { fill: red; }");
        text.ShouldContain("<rect width=\"5\" height=\"5\" />");
        text.ShouldContain("<text>link</text>");
        text.ShouldContain("data:image/png");
        text.ShouldContain("attributeName=\"opacity\"");
        text.ShouldContain("<circle r=\"1\"");
        foreach (var gone in (string[])["@import", "javascript", "onmouseover", "OnLoad", "data:image/svg", "onclick", "alert(6)", "inkscape:label"])
        {
            text.ShouldNotContain(gone);
        }
    }

    [Theory]
    [InlineData("""<!DOCTYPE svg [<!ENTITY x "y">]><svg xmlns="http://www.w3.org/2000/svg">&x;</svg>""")]
    [InlineData("""<html xmlns="http://www.w3.org/1999/xhtml"><body/></html>""")]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg"><rect></svg>""")]
    public void ADocumentWithADtdAnotherRootOrBrokenMarkupIsNoSvg(string xml) =>
        SvgDocument.Parse(new MemoryStream(Encoding.UTF8.GetBytes(xml))).ShouldBeNull();

    [Fact]
    public async Task TheLocalStorageWritesReadsReplacesAndDeletesUnderItsRoot()
    {
        var root = Directory.CreateTempSubdirectory("fluentcms-media-storage-");
        try
        {
            var storage = Storage(root.FullName + Path.DirectorySeparatorChar);
            const string key = "site/asset/original.png";

            await storage.WriteAsync(key, new MemoryStream([1, 2, 3]), Token);
            await storage.WriteAsync(key, new MemoryStream([4, 5]), Token);

            (await storage.ExistsAsync(key, Token)).ShouldBeTrue();
            (await storage.GetMetadataAsync(key, Token)).ShouldNotBeNull().Size.ShouldBe(2);
            await using (var stream = (await storage.OpenReadAsync(key, Token)).ShouldNotBeNull())
            {
                using var copy = new MemoryStream();
                await stream.CopyToAsync(copy, Token);
                copy.ToArray().ShouldBe([4, 5]);
            }

            Directory.EnumerateFiles(root.FullName, "*", SearchOption.AllDirectories).ShouldHaveSingleItem().ShouldEndWith("original.png");
            (await storage.DeleteAsync(key, Token)).ShouldBeTrue();
            (await storage.DeleteAsync(key, Token)).ShouldBeFalse();
            (await storage.OpenReadAsync(key, Token)).ShouldBeNull();
            (await storage.GetMetadataAsync(key, Token)).ShouldBeNull();
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("../outside.png")]
    [InlineData("a/../../outside.png")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\win.ini")]
    [InlineData("a//b")]
    [InlineData("..")]
    [InlineData("a/b c")]
    [InlineData("")]
    public async Task AKeyThatIsNotPlainSegmentsIsRefused(string key)
    {
        var storage = Storage(Path.GetTempPath());

        await Should.ThrowAsync<ArgumentException>(() => storage.ExistsAsync(key, Token));
        await Should.ThrowAsync<ArgumentException>(() => storage.WriteAsync(key, new MemoryStream([1]), Token));
    }

    private static LocalFileStorage Storage(string root) =>
        new(Options.Create(new MediaOptions { Local = new LocalStorageOptions { Root = root } }), new TestEnvironment());

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
