using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
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

        text.ShouldContain("<rect width=\"5\" height=\"5\" />");
        text.ShouldContain("<g><text>link</text></g>");
        text.ShouldContain("attributeName=\"opacity\"");
        text.ShouldContain("<circle r=\"1\"");
        foreach (var gone in (string[])["<style", "fill: red", "@import", "javascript", "onmouseover", "OnLoad", "<image", "data:image", "onclick", "alert(6)", "inkscape:label"])
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

    // A page that inlines the SVG would apply a <style> to itself, and find an id on window and in getElementById, so a
    // <style> goes whatever it holds, and every id and reference takes the prefix of the file: none names the page's
    // globals, its elements or another SVG's ids, a reference to no id of the file included.
    [Fact]
    public void AnInlinedSvgReachesNoStyleNameOrElementOfThePage()
    {
        var svg = SvgDocument.Parse(new MemoryStream(Files.Svg("""
            <style>body { display: none } svg { position: fixed }</style>
            <g id="config"><rect id="location" width="1" height="1" style="fill: red"/></g>
            <use href="#login-form"/>
            <rect width="1" height="1" fill="url(#page-gradient)" aria-labelledby="page-label"/>
            <animate attributeName="opacity" to="0" begin="page-button.click" dur="1s"/>
            """))).ShouldNotBeNull();

        SvgDocument.Sanitize(svg);
        SvgDocument.Scope(svg);
        var text = Encoding.UTF8.GetString(SvgDocument.Save(svg));

        var prefix = Prefix(text);
        text.ShouldNotContain("<style");
        text.ShouldNotContain("display: none");
        text.ShouldContain($"""<rect id="{prefix}location" width="1" height="1" style="fill: red" />""");
        text.ShouldContain($"""<g id="{prefix}config">""");
        text.ShouldContain($"""<use href="#{prefix}login-form" />""");
        text.ShouldContain($"""fill="url(#{prefix}page-gradient)" aria-labelledby="{prefix}page-label" """);
        text.ShouldContain($"""begin="{prefix}page-button.click" """);
        foreach (var name in Names(text))
        {
            name.ShouldStartWith(prefix);
        }
    }

    // A drawing that refers to its own elements in every way an SVG can still finds each of them, and a clock value, an
    // event of the element itself and a quoted url() keep their meaning.
    [Fact]
    public void ScopingKeepsEveryReferenceOfTheSvgToItsOwnElements()
    {
        var svg = SvgDocument.Parse(new MemoryStream(Files.Svg("""
            <title id="t">Logo</title>
            <defs>
              <linearGradient id="g"><stop offset="0"/></linearGradient>
              <linearGradient id="g2" href="#g"/>
              <clipPath id="c"><rect width="5" height="5"/></clipPath>
              <mask id="m"><rect width="5" height="5" fill="white"/></mask>
              <marker id="k"><path d="M0 0"/></marker>
              <filter id="f"><feGaussianBlur stdDeviation="1"/></filter>
              <path id="p" d="M0 0 L5 5"/>
              <circle id="dot" r="1"/>
            </defs>
            <g aria-labelledby="t" clip-path="url(#c)" mask="url( '#m' )" filter="URL(#f)">
              <use xlink:href=" #dot"/>
              <path d="M0 0" marker-end="url(#k)" style="stroke: url(&quot;#g2&quot;)"/>
              <text><textPath href="#p">x</textPath></text>
              <animate id="a" attributeName="opacity" values="0;1" dur="1s"/>
              <animate attributeName="fill" values="url(#g);url(#g2)" begin="a.end+1.5s; 2s; click; a.repeat(2)" end="indefinite" dur="1s"/>
            </g>
            """))).ShouldNotBeNull();

        SvgDocument.Sanitize(svg);
        SvgDocument.Scope(svg);
        var text = Encoding.UTF8.GetString(SvgDocument.Save(svg));

        var prefix = Prefix(text);
        var ids = Ids(text, prefix);
        ids.ShouldBe(["t", "g", "g2", "c", "m", "k", "f", "p", "dot", "a"], ignoreOrder: true);
        var names = Names(text);
        names.Count.ShouldBe(13);
        foreach (var name in names)
        {
            name.ShouldStartWith(prefix);
            ids.ShouldContain(name[prefix.Length..]);
        }

        Sanitized(Encoding.UTF8.GetBytes(text)).ShouldBe(Encoding.UTF8.GetBytes(text));
        text.ShouldContain($"""begin="{prefix}a.end+1.5s; 2s; click; {prefix}a.repeat(2)" end="indefinite" """);
        text.ShouldContain($"""xlink:href="#{prefix}dot" """);
    }

    // Sanitising a file the pass wrote changes nothing, byte for byte, so that an import can take its key, a file without ids
    // and one that took a prefix on top of the uploader's included. A prefix of the form that the uploader wrote, with a
    // reference without it, is not the file's own, so every name takes the file's on top.
    [Fact]
    public void ScopingAScopedFileChangesNothingAndAPrefixTheFileDoesNotDeriveTakesItsOwnOnTop()
    {
        foreach (var body in (string[])[
            """<defs><circle id="dot" r="1"/></defs><use href="#dot"/><rect width="1" height="1" fill="url(#dot)" aria-labelledby="dot"/><animate attributeName="opacity" to="0" begin="dot.click" dur="1s"/>""",
            """<use href="#page"/>""",
            """<circle id="fcms_0badf00d0badf00d_dot" r="1"/><use href="#fcms_0badf00d0badf00d_dot"/>""",
            "<title>a&#xD;b</title><text xml:space=\"preserve\">line\n  <tspan>x</tspan></text>"])
        {
            var once = Sanitized(Files.Svg(body));

            Sanitized(once).ShouldBe(once, body);
        }

        var text = Encoding.UTF8.GetString(Sanitized(Files.Svg("""<circle id="fcms_0badf00d0badf00d_dot" r="1"/><use href="#fcms_0badf00d0badf00d_dot"/><use href="#page"/>""")));
        var prefix = Prefix(text);
        prefix.ShouldNotBe("fcms_0badf00d0badf00d_");
        text.ShouldContain($"""<use href="#{prefix}fcms_0badf00d0badf00d_dot" />""");
        text.ShouldContain($"""<use href="#{prefix}page" />""");
    }

    // A page that inlines the stored file parses it as HTML, where a CDATA section or a comment ends at the first > and a
    // prefixed element is no SVG, so what the file holds after it would be markup of the page. What is stored holds none of
    // those, only SVG elements without a prefix, and text and values escaped.
    public static TheoryData<string> Constructs => new()
    {
        """<s:svg xmlns:s="http://www.w3.org/2000/svg"><s:text><![CDATA[><img src=x onerror=alert(document.domain)>]]></s:text></s:svg>""",
        Encoding.UTF8.GetString(Files.Svg("""<title><![CDATA[><img src=x onerror=alert(1)>]]></title>""")),
        Encoding.UTF8.GetString(Files.Svg("""<desc><![CDATA[><img src=x onerror=alert(1)>]]></desc>""")),
        Encoding.UTF8.GetString(Files.Svg("""<text><tspan><![CDATA[><img src=x onerror=alert(1)>]]></tspan></text>""")),
        Encoding.UTF8.GetString(Files.Svg("""<path id="p" d="M0 0"/><text><textPath href="#p"><![CDATA[><img src=x onerror=alert(1)>]]></textPath></text>""")),
        Encoding.UTF8.GetString(Files.Svg("""<!-- <img src=x onerror=alert(1)> --><rect width="1" height="1"/>""")),
        Encoding.UTF8.GetString(Files.Svg("""<?pi <img src=x onerror=alert(1)>?><rect width="1" height="1"/>""")),
        """<s:svg xmlns:s="http://www.w3.org/2000/svg" xmlns="http://www.w3.org/2000/svg" xmlns:t="http://www.w3.org/2000/svg"><rect width="1" height="1"/><t:circle r="1"/><s:g><t:text>&lt;img src=x onerror=alert(1)&gt;</t:text></s:g></s:svg>""",
    };

    [Theory]
    [MemberData(nameof(Constructs))]
    public void WhatIsStoredMeansToAnHtmlParserWhatItMeansAsXml(string xml)
    {
        var text = Encoding.UTF8.GetString(Sanitized(Encoding.UTF8.GetBytes(xml)));

        text.ShouldStartWith("<svg xmlns=\"http://www.w3.org/2000/svg\"");
        foreach (var construct in (string[])["<![CDATA[", "<!--", "<?", "<img", "<!"])
        {
            text.ShouldNotContain(construct);
        }

        Regex.IsMatch(text, @"</?[A-Za-z][\w.-]*:").ShouldBeFalse(text);
        XDocument.Parse(text).Root.ShouldNotBeNull().Name.ShouldBe(SvgDocument.SvgNamespace + "svg");
    }

    // A text with every character that ends a context in HTML, and new lines, which are written as \n on every platform so
    // that a file sanitises to the same bytes wherever it is.
    [Fact]
    public void TextAndValuesAreEscapedAndNewLinesAreTheSameOnEveryPlatform()
    {
        var text = Encoding.UTF8.GetString(Sanitized(Files.Svg("<title>a &lt;/title&gt; &amp; \"b\" 'c'\nd&#xD;</title><rect width=\"1\" height=\"1\" aria-label=\"&quot;&gt;&lt;img&#xA;x\"/>")));

        text.ShouldContain("<title>a &lt;/title&gt; &amp; \"b\" 'c'\nd&#xD;</title>");
        text.ShouldContain("aria-label=\"&quot;&gt;&lt;img&#xA;x\"");
        text.ShouldNotContain("\r");
    }

    // The uploader may write ids and references that carry a prefix of the form, such as another SVG's, read from its file;
    // it is kept only when it is the one the file derives without it, so it takes the file's own on top.
    [Theory]
    [InlineData("""<defs><linearGradient id="fcms-0badf00d-grad"/></defs><rect width="1" height="1" fill="url(#fcms-0badf00d-grad)"/>""", "fcms-0badf00d-grad")]
    [InlineData("""<defs><linearGradient id="fcms_0badf00d0badf00d_grad"/></defs><rect width="1" height="1" fill="url(#fcms_0badf00d0badf00d_grad)"/>""", "fcms_0badf00d0badf00d_grad")]
    [InlineData("""<use href="#fcms-0badf00d-icon"/>""", "fcms-0badf00d-icon")]
    [InlineData("""<use href="#fcms_0badf00d0badf00d_icon"/>""", "fcms_0badf00d0badf00d_icon")]
    public void APrefixTheUploaderWroteIsNotKept(string body, string name)
    {
        var text = Encoding.UTF8.GetString(Sanitized(Files.Svg(body)));

        text.ShouldNotContain($"\"{name}\"");
        text.ShouldNotContain($"#{name}");
        Regex.IsMatch(text, $@"fcms_[0-9a-f]{{16}}_{Regex.Escape(name)}").ShouldBeTrue(text);
    }

    // A browser that reads the escapes of a begin or end value would time the animation by an element of the page, so a value
    // with a backslash goes.
    [Fact]
    public void ABeginOrEndWithABackslashGoes()
    {
        var text = Encoding.UTF8.GetString(Sanitized(Files.Svg("""<animate attributeName="opacity" to="0" begin="\submit.click; pay\-now.click" end="\x.end" dur="1s"/>""")));

        text.ShouldContain("<animate attributeName=\"opacity\" to=\"0\" dur=\"1s\" />");
        text.ShouldNotContain("\\");
    }

    // The root's overflow, style and transform would let the drawing paint over the page around it; the elements inside
    // keep theirs, which the root clips.
    [Fact]
    public void TheRootKeepsNoOverflowStyleOrTransform()
    {
        var text = Encoding.UTF8.GetString(Sanitized(Encoding.UTF8.GetBytes("""<svg xmlns="http://www.w3.org/2000/svg" overflow="visible" width="1" height="1" transform="translate(-500 -500)" filter="drop-shadow(0 0 5000px white)" style="position:fixed;inset:0;z-index:99999"><g style="opacity: 0.5" overflow="visible"><rect x="-5000" y="-5000" width="10000" height="10000" fill="white"/></g></svg>""")));

        text.ShouldStartWith("""<svg xmlns="http://www.w3.org/2000/svg" width="1" height="1">""");
        text.ShouldContain("""<g style="opacity: 0.5" overflow="visible"><rect x="-5000" y="-5000" width="10000" height="10000" fill="white" /></g>""");
    }

    // An animation of the root, a child without a link or one whose link is the root's id, that sets its overflow, style,
    // transform or filter, or moves it, would take the drawing out of its box; the same animations of an element inside stay.
    [Fact]
    public void AnAnimationThatMovesTheRootOutOfItsBoxGoes()
    {
        var text = Encoding.UTF8.GetString(Sanitized(Encoding.UTF8.GetBytes("""
            <svg xmlns="http://www.w3.org/2000/svg" id="root" width="1" height="1">
            <set attributeName="overflow" to="visible"/>
            <animateTransform attributeName="transform" type="translate" to="-500 -500" dur="1s"/>
            <animateMotion path="M0 0 L-500 -500" dur="1s"/>
            <animate attributeName="style" to="position:fixed" dur="1s"/>
            <g><animate href="#root" attributeName="filter" to="drop-shadow(0 0 5000px white)" dur="1s"/></g>
            <set attributeName="opacity" to="0.5"/>
            <g><animateTransform attributeName="transform" type="rotate" to="90" dur="1s"/></g>
            </svg>
            """)));

        foreach (var gone in (string[])["overflow", "translate", "animateMotion", "position", "drop-shadow"])
        {
            text.ShouldNotContain(gone);
        }

        text.ShouldContain("""<set attributeName="opacity" to="0.5" />""");
        text.ShouldContain("""<g><animateTransform attributeName="transform" type="rotate" to="90" dur="1s" /></g>""");
    }

    // A file nests at most MaxDepth levels of elements, so that no pass over its tree, which recurses, runs out of stack or
    // takes a time that grows with the square of its depth; one deeper is no SVG, which the reader finds before it builds
    // the tree, however large the file.
    [Fact]
    public void AFileNestedToTheLimitIsTakenAndOneNestedDeeperIsNot()
    {
        static byte[] Nested(int levels) => Encoding.UTF8.GetBytes(
            "<svg xmlns=\"http://www.w3.org/2000/svg\">" + string.Concat(Enumerable.Repeat("<g>", levels - 1)) + "x" + string.Concat(Enumerable.Repeat("</g>", levels - 1)) + "</svg>");

        var text = Encoding.UTF8.GetString(Sanitized(Nested(SvgDocument.MaxDepth)));
        Regex.Count(text, "<g>").ShouldBe(SvgDocument.MaxDepth - 1);
        SvgDocument.Parse(new MemoryStream(Nested(SvgDocument.MaxDepth + 1))).ShouldBeNull();

        var deep = Nested(100_000);
        deep.Length.ShouldBeGreaterThan(700_000);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        SvgDocument.Parse(new MemoryStream(deep)).ShouldBeNull();
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(2));
    }

    // Each value of a begin or end list is read whole, or the list goes: a name the timing grammar does not read, which a
    // browser may read as an element of the page, is never left without the prefix.
    [Theory]
    [InlineData("foo(.click")]
    [InlineData("pg x.click")]
    [InlineData("pg.1click")]
    [InlineData("2s; b(.end")]
    [InlineData("a..click")]
    [InlineData("\\submit.click")]
    public void ABeginOrEndThatIsNotReadWholeGoes(string begin)
    {
        var text = Encoding.UTF8.GetString(Sanitized(Files.Svg($"""<animate id="a" attributeName="opacity" to="0" begin="{begin}" end="{begin}" dur="1s"/>""")));

        text.ShouldNotContain("begin=");
        text.ShouldNotContain("end=");
        text.ShouldContain("dur=\"1s\"");
    }

    [Fact]
    public void EveryFormOfABeginOrEndListIsReadAndItsIdsTakeThePrefix()
    {
        var text = Encoding.UTF8.GetString(Sanitized(Files.Svg("""<animate id="a" attributeName="opacity" to="0" begin="a.end+1.5s; 2s; -0.5s; 1:02:03.5; click; a.repeat(2); accessKey(x); wallclock(2026-01-01T00:00:00Z); indefinite; a.begin - 2min" dur="1s"/>""")));

        var prefix = Prefix(text);
        text.ShouldContain($"""begin="{prefix}a.end+1.5s; 2s; -0.5s; 1:02:03.5; click; {prefix}a.repeat(2); accessKey(x); wallclock(2026-01-01T00:00:00Z); indefinite; {prefix}a.begin - 2min" """);
    }

    // The uploader can give the ids the file's own prefix, which the pass would derive, and leave the references plain, or
    // name another stored file's id; the prefix is kept only when scoping the file again gives the same bytes, so here every
    // name takes the file's own on top, and the result is sanitised to the same bytes again.
    [Fact]
    public void IdsWithTheFilesOwnPrefixAndPlainReferencesTakeAPrefixOnEveryName()
    {
        const string Body = """<defs><linearGradient id="g"/></defs><use href="#victim"/><use href="#fcms_0123456789abcdef_icon"/><rect width="1" height="1" fill="url(#victim)" aria-labelledby="victim"/><animate id="a" attributeName="opacity" to="0" begin="victim.click" dur="1s"/>""";
        var derived = Prefix(Encoding.UTF8.GetString(Sanitized(Files.Svg(Body))));
        var crafted = Files.Svg(Body.Replace("id=\"g\"", $"id=\"{derived}g\"", StringComparison.Ordinal).Replace("id=\"a\"", $"id=\"{derived}a\"", StringComparison.Ordinal));

        var once = Sanitized(crafted);

        var text = Encoding.UTF8.GetString(once);
        var own = Prefix(text);
        var names = Names(text);
        names.Count.ShouldBe(5);
        foreach (var name in names)
        {
            name.ShouldStartWith(own);
        }

        Ids(text, own).ShouldBe([$"{derived}g", $"{derived}a"], ignoreOrder: true);
        var twice = Sanitized(once);
        twice.ShouldBe(once);
        Sanitized(twice).ShouldBe(once);
    }

    // element() paints an element of the page, and an animation of class or id gives an element of the SVG a name of the
    // page's.
    [Fact]
    public void ACopyOfAnElementOfThePageAndAnAnimationOfClassOrIdGo()
    {
        var text = Encoding.UTF8.GetString(Sanitized(Files.Svg("""
            <rect width="1" height="1" style="fill: red; background: -moz-element(#login)"/>
            <rect width="2" height="2" fill="element(#login)"/>
            <set attributeName="class" to="admin"/>
            <animate attributeName="id" values="a;b" dur="1s"/>
            <set attributeName="opacity" to="1"/>
            """)));

        text.ShouldContain("""<rect width="1" height="1" />""");
        text.ShouldContain("""<rect width="2" height="2" />""");
        text.ShouldContain("""attributeName="opacity" """);
        foreach (var gone in (string[])["element(", "\"class\"", "\"id\"", "admin"])
        {
            text.ShouldNotContain(gone);
        }
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

    private static byte[] Sanitized(byte[] bytes)
    {
        var svg = SvgDocument.Parse(new MemoryStream(bytes)).ShouldNotBeNull();
        SvgDocument.Sanitize(svg);
        SvgDocument.Scope(svg);
        return SvgDocument.Save(svg);
    }

    // The prefix the file took, the first in it.
    private static string Prefix(string text)
    {
        var prefix = Regex.Match(text, "fcms_[0-9a-f]{16}_").Value;
        prefix.ShouldNotBeNullOrEmpty();
        return prefix;
    }

    // The ids, each of which must carry the prefix, without it.
    private static List<string> Ids(string text, string prefix)
    {
        List<string> ids = [];
        foreach (var id in XDocument.Parse(text).Descendants().Select(element => element.Attribute("id")?.Value).OfType<string>())
        {
            id.ShouldStartWith(prefix);
            ids.Add(id[prefix.Length..]);
        }

        return ids;
    }

    // Each name that a link, a url(), an aria id list or the begin or end of an animation refers to.
    private static List<string> Names(string text)
    {
        List<string> names = [];
        foreach (var attribute in XDocument.Parse(text).Descendants().SelectMany(element => element.Attributes()))
        {
            var value = attribute.Value.Trim();
            if (attribute.Name.LocalName == "href")
            {
                names.Add(value[1..]);
            }
            else if (attribute.Name.LocalName is "aria-labelledby" or "aria-describedby")
            {
                names.AddRange(value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            }
            else if (attribute.Name.LocalName is "begin" or "end")
            {
                names.AddRange(value.Split(';').Select(timing => timing.Trim()).Where(timing => timing.Contains('.', StringComparison.Ordinal) && char.IsLetter(timing[timing.IndexOf('.', StringComparison.Ordinal) + 1])).Select(timing => timing[..timing.IndexOf('.', StringComparison.Ordinal)]));
            }
            else
            {
                names.AddRange(Regex.Matches(value, @"url\(['""]?#([^'"")]+)").Select(match => match.Groups[1].Value));
            }
        }

        return names;
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
