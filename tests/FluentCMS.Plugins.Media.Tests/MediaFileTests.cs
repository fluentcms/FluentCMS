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
        text.ShouldContain("<g><text>link</text></g>");
        text.ShouldContain("attributeName=\"opacity\"");
        text.ShouldContain("<circle r=\"1\"");
        foreach (var gone in (string[])["@import", "javascript", "onmouseover", "OnLoad", "<image", "data:image", "onclick", "alert(6)", "inkscape:label"])
        {
            text.ShouldNotContain(gone);
        }
    }

    // A reference out of the document in the spellings a browser still follows: in an attribute name of any case, after
    // white space and entities, protocol-relative, in a data: URL of another type, in url() with escapes, quotes, white
    // space and capitals, in CSS, in an animation, under an xml:base and in a processing instruction.
    [Theory]
    [InlineData("""<image href="https://evil.example/a.png"/>""")]
    [InlineData("""<image xlink:href="https://evil.example/a.png"/>""")]
    [InlineData("""<use href="https://evil.example/s.svg#a"/>""")]
    [InlineData("""<use xlink:href="//evil.example/s.svg#a"/>""")]
    [InlineData("""<a HREF="https://evil.example/"><text>x</text></a>""")]
    [InlineData("""<image href=" &#x09; &#x68;ttps://evil.example/a.png"/>""")]
    [InlineData("""<image href="HTTPS://EVIL.EXAMPLE/a.png"/>""")]
    [InlineData("""<image href="data:text/html,&lt;script src=https://evil.example/x.js&gt;&lt;/script&gt;"/>""")]
    [InlineData("""<filter id="f"><feImage href="https://evil.example/a.png"/></filter>""")]
    [InlineData("""<rect fill="url(https://evil.example/p.svg#p)"/>""")]
    [InlineData("""<rect fill="URL( 'https://evil.example/p.svg#p' )"/>""")]
    [InlineData("""<rect fill="u\72l(https://evil.example/p.svg#p)"/>""")]
    [InlineData("""<rect fill="url(&#x68;ttps://evil.example/p.svg#p)"/>""")]
    [InlineData("""<rect style="fill: url(//evil.example/p.svg#p)"/>""")]
    [InlineData("""<rect style="filter: \75 rl(https://evil.example/f.svg#f)"/>""")]
    [InlineData("""<style>rect { fill: url("https://evil.example/p.svg#p") }</style>""")]
    [InlineData("""<style>rect { fill: url( https://evil.example/p.svg#p ) }</style>""")]
    [InlineData("""<style>@font-face { font-family: f; src: url(https://evil.example/f.woff) }</style>""")]
    [InlineData("""<style>rect { background: image-set("https://evil.example/a.png" 1x) }</style>""")]
    [InlineData("""<animate attributeName="fill" values="red;url(https://evil.example/p.svg#p)" dur="1s"/>""")]
    [InlineData("""<g xml:base="https://evil.example/"><use href="#kept"/></g>""")]
    [InlineData("""<use href="&#xA0;#evil.example"/>""")]
    [InlineData("""<rect width="1" height="1" fill='url(\"#evil.example\")'/>""")]
    [InlineData("""<rect width="1" height="1" fill="url(\'#evil.example\')"/>""")]
    [InlineData("""<rect width="1" height="1" fill="url(\22#evil.example\22)"/>""")]
    [InlineData("""<rect width="1" height="1" style='fill: url(\"#evil.example\")'/>""")]
    [InlineData("""<style>rect { fill: url(\"#evil.example\") }</style>""")]
    [InlineData("""<animate attributeName="fill" values='red;url(\"#evil.example\")' dur="1s"/>""")]
    [InlineData("""<animate attributeName="fill" from='url(\"#evil.example\")' dur="1s"/>""")]
    [InlineData("""<animate attributeName="fill" to='url(\"#evil.example\")' dur="1s"/>""")]
    [InlineData("""<animate attributeName="fill" by='url(\"#evil.example\")' dur="1s"/>""")]
    [InlineData("""<?xml-stylesheet href="https://evil.example/x.css"?><rect width="1" height="1"/>""")]
    public void SanitisingLeavesNoReferenceOutOfTheDocument(string body)
    {
        var svg = SvgDocument.Parse(new MemoryStream(Files.Svg(body + """<circle id="kept" r="1"/>"""))).ShouldNotBeNull();

        SvgDocument.Sanitize(svg);
        var text = Encoding.UTF8.GetString(SvgDocument.Save(svg));

        text.ShouldContain("""<circle id="kept" r="1" />""");
        text.ShouldNotContain("evil.example", Case.Insensitive);
    }

    [Fact]
    public void AFragmentOfTheDocumentStaysAndAnImageInADataUrlGoes()
    {
        var svg = SvgDocument.Parse(new MemoryStream(Files.Svg("""
            <defs><linearGradient id="g"><stop offset="0"/></linearGradient><circle id="dot" r="1"/></defs>
            <use href="#dot"/>
            <use xlink:href=" #dot"/>
            <rect width="1" height="1" fill="url(#g)" style="stroke: url('#g')"/>
            <image href="data:image/png;base64,iVBORw0KGgo=" width="1" height="1"/>
            """))).ShouldNotBeNull();

        SvgDocument.Sanitize(svg);
        var text = Encoding.UTF8.GetString(SvgDocument.Save(svg));

        text.ShouldContain("""<use href="#dot" />""");
        text.ShouldContain("xlink:href=\" #dot\"");
        text.ShouldContain("fill=\"url(#g)\" style=\"stroke: url('#g')\"");
        text.ShouldNotContain("data:image");
    }

    // What the review of #3309 found past a pass by names it drops: a string that hides a comment, a comment that hides a
    // url(), a link that is a fragment only once CSS reads it, HTML elements and attributes that load a URL, and each of
    // those inside <title> and <desc>, whose content an HTML page that inlines the SVG parses as HTML.
    public static TheoryData<string, string> Found
    {
        get
        {
            (string Body, string Gone)[] found =
            [
                ("""<style>rect{--a:"/*"}rect{fill:url(https://evil.example/p.svg#p)}rect{--b:"*/"}</style>""", "evil.example"),
                ("""<rect width="1" height="1" style='--a:"/*";fill:url(https://evil.example/p.svg#p);--b:"*/"'/>""", "evil.example"),
                ("""<style>@charset "/*";@import "https://evil.example/x.css";/*"*/</style>""", "evil.example"),
                ("""<animate attributeName="fill" values="red;/*;url(https://evil.example/p#p);*/" dur="1s"/>""", "evil.example"),
                ("""<use href="/*x*/#a"/>""", "/*x*/"),
                ("""<use href="\23 x"/>""", "\\23"),
                ("""<rect width="1" height="1" fill="url(/*x*/#a)"/>""", "/*x*/"),
                ("""<img src="https://evil.example/a.png"/>""", "evil.example"),
                ("""<meta http-equiv="refresh" content="0;url=https://evil.example/"/>""", "evil.example"),
                ("""<table background="https://evil.example/a.png"/>""", "evil.example"),
                ("""<rect width="1" height="1" srcset="https://evil.example/a.png 1x"/>""", "evil.example"),
                ("""<rect width="1" height="1" mask="image-set('https://evil.example/a.png' 1x)"/>""", "evil.example"),
                ("""<set attributeName="x:onclick" to="alert(1)"/>""", "x:onclick"),
            ];

            var data = new TheoryData<string, string>();
            foreach (var (body, gone) in found)
            {
                data.Add(body, gone);
                data.Add($"<title>{body}</title>", gone);
                data.Add($"<desc>{body}</desc>", gone);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Found))]
    public void SanitisingKeepsOnlyWhatItsListsName(string body, string gone)
    {
        var svg = SvgDocument.Parse(new MemoryStream(Files.Svg(body + """<circle id="kept" r="1"/>"""))).ShouldNotBeNull();

        SvgDocument.Sanitize(svg);
        var text = Encoding.UTF8.GetString(SvgDocument.Save(svg));

        text.ShouldContain("""<circle id="kept" r="1" />""");
        text.ShouldNotContain(gone, Case.Insensitive);
    }

    // An element of no list goes whatever its case, since an HTML page that inlines the SVG reads SCRIPT as script, and the
    // text of a <title> stays.
    [Fact]
    public void AnElementOrAttributeOfNoListGoesInAnyCaseAndTitlesKeepTheirText()
    {
        var svg = SvgDocument.Parse(new MemoryStream(Files.Svg("""<SCRIPT>alert(1)</SCRIPT><Style>rect{}</Style><rect width="1" height="1" Fill="red" ONCLICK="alert(2)"/><title>A &lt;b&gt;logo&lt;/b&gt;</title>"""))).ShouldNotBeNull();

        SvgDocument.Sanitize(svg);
        var text = Encoding.UTF8.GetString(SvgDocument.Save(svg));

        text.ShouldContain("""<rect width="1" height="1" />""");
        text.ShouldContain("<title>A &lt;b&gt;logo&lt;/b&gt;</title>");
        foreach (var removed in (string[])["SCRIPT", "alert", "Style", "Fill", "ONCLICK"])
        {
            text.ShouldNotContain(removed);
        }
    }

    // @import in capitals, with CSS escapes of hex digits, short and long, ended by a space or not, of a letter that is
    // no hex digit, and with an XML entity.
    [Theory]
    [InlineData("""@import url(https://evil.example/x.css);""")]
    [InlineData("""@IMPORT "https://evil.example/x.css";""")]
    [InlineData("""@\69mport "https://evil.example/x.css";""")]
    [InlineData("""@\000069mport "https://evil.example/x.css";""")]
    [InlineData("""@\49 MPORT "https://evil.example/x.css";""")]
    [InlineData("""@im\port "https://evil.example/x.css";""")]
    [InlineData("""@&#x69;mport "https://evil.example/x.css";""")]
    public void AnImportIsRemovedHoweverItIsEscaped(string import)
    {
        var svg = SvgDocument.Parse(new MemoryStream(Files.Svg($$"""<style>{{import}} rect { fill: red; }</style><circle id="kept" r="1"/>"""))).ShouldNotBeNull();

        SvgDocument.Sanitize(svg);
        var text = Encoding.UTF8.GetString(SvgDocument.Save(svg));

        text.ShouldNotContain("<style");
        text.ShouldNotContain("evil.example", Case.Insensitive);
        text.ShouldContain("""<circle id="kept" r="1" />""");
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

    [Theory]
    [InlineData("Production", true)]
    [InlineData("Staging", true)]
    [InlineData("Development", false)]
    public void TheLocalDiskAndUnsignedUrlsAreReportedOutsideDevelopment(string environment, bool reported)
    {
        var options = Options.Create(new MediaOptions { RequireSignedUrls = false });
        var source = new MediaInsecureDefaults(Storage(Path.GetTempPath()), new TestEnvironment { EnvironmentName = environment }, options);

        var ids = source.GetInsecureDefaults().Select(item => item.Id).ToList();

        ids.ShouldBe(reported ? [MediaInsecureDefaults.LocalStorage, MediaInsecureDefaults.UnsignedUrls] : []);
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
