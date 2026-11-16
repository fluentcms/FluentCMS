using System.IO.Compression;
using System.Text;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// A trace is what a failed test leaves when traces are on, and CI uploads it; it must hold no password and no token.
public sealed class TraceTests(EndToEndHost host) : BrowserTest(host)
{
    private const string Password = "Aa1!0123456789ABCDEF";

    // A cookie's value the context saw, which no pattern finds.
    private const string Cookie = "opaque-session-1234567890";

    private readonly DirectoryInfo _directory = host.NewDirectory();

    [Fact]
    public void TheMaskKeepsOnlyItsAllowlistAndMasksPasswordsTokensAndCookies()
    {
        var source = Path.Combine(_directory.FullName, "source.zip");
        using (var archive = ZipFile.Open(source, ZipArchiveMode.Create))
        {
            Write(archive, "trace.trace", $$$"""
                {"type":"before","method":"fill","params":{"selector":"#Input_Password","value":"{{{Password}}}"}}
                {"type":"frame-snapshot","html":["INPUT",{"name":"__RequestVerificationToken","value":"CfDJ8AbCdEfGhIjKlMnOpQrStUvWxYz0123"}]}
                {"type":"event","url":"https://127.0.0.1/admin/authentication/login-callback?code=eyJhbGciOiJSU0EtT0FFUCJ9.a.b.c.d&state=s1"}
                {"type":"log","message":"code_verifier=verifier1&grant_type=authorization_code&Input.Password=Aa1%210123456789ABCDEF"}
                """);
            Write(archive, "trace.network", """{"request":{"postData":{"text":"password=x"}}}""");
            Write(archive, "trace.stacks", "{\"cookie\":\"" + Cookie + "\"}");
            Write(archive, "other.json", "{}");
            Write(archive, "screencast/nested/page@2.jpeg", "jpeg");
            Write(archive, "resources/0a1b.json", """{"access_token":"eyJhbGciOiJSUzI1NiJ9.e30.sig"}""");
            Write(archive, "resources/2c3d.html", "<input value=\"x\">");
            Write(archive, "screencast/page@1.jpeg", "ÿØ jpeg");
            Write(archive, "resources/4e5f.css", "body { color: red }");
        }

        // A trace left at the same path, as by a test that failed twice, is replaced.
        var masked = Path.Combine(_directory.FullName, "masked.zip");
        TraceScrubber.Scrub(source, masked, [Password, Cookie]);
        TraceScrubber.Scrub(source, masked, [Password, Cookie]);

        using var result = ZipFile.OpenRead(masked);
        result.Entries.Select(entry => entry.FullName).ShouldBe(["trace.trace", "trace.stacks", "screencast/page@1.jpeg", "resources/4e5f.css"], ignoreOrder: true);
        var trace = Read(result.GetEntry("trace.trace")!);
        trace.ShouldNotContain(Password);
        trace.ShouldNotContain("eyJ");
        trace.ShouldNotContain("CfDJ8");
        trace.ShouldNotContain("verifier1");
        trace.ShouldNotContain("Aa1%21");
        trace.ShouldContain("state=s1");
        trace.ShouldContain("grant_type=authorization_code");
        Read(result.GetEntry("trace.stacks")!).ShouldNotContain(Cookie);
        TraceScrubber.Leaks(masked, [Password, Cookie]).ShouldBeNull();
        TraceScrubber.Leaks(source, [Password, Cookie]).ShouldNotBeNull();
    }

    // A secret the mask cannot find by pattern, in an entry it does not rewrite, keeps the trace from being kept at all.
    [Fact]
    public void ATraceThatStillHoldsASecretIsNotKept()
    {
        var source = Path.Combine(_directory.FullName, "source.zip");
        using (var archive = ZipFile.Open(source, ZipArchiveMode.Create))
        {
            Write(archive, "trace.trace", "{}");
            Write(archive, "screencast/page@1.jpeg", "jpeg " + Cookie);
        }

        var masked = Path.Combine(_directory.FullName, "masked.zip");
        Should.Throw<InvalidOperationException>(() => TraceScrubber.Scrub(source, masked, [Cookie]));
        File.Exists(masked).ShouldBeFalse();
    }

    // The trace of a real sign-in, saved as a failed test saves it: screenshots and snapshots, and nothing of the network.
    [Fact]
    public async Task TheTraceOfASignInHoldsNoPasswordOrToken()
    {
        var trace = Path.Combine(_directory.FullName, "sign-in.zip");
        var browser = await OpenBrowserAsync(traces: _directory.FullName);
        var tab = await browser.NewTabAsync();
        await tab.Page.GotoAsync(Host.Admin.AbsoluteUri);
        await browser.SignInOnTheHostedPageAsync(tab, Host.Editor);
        await Expect(tab.Sidebar).ToBeVisibleAsync();
        await tab.ReloadAsync();
        await Expect(tab.Sidebar).ToBeVisibleAsync();
        await browser.SaveTraceAsync(trace);

        using var archive = ZipFile.OpenRead(trace);
        var names = archive.Entries.Select(entry => entry.FullName).ToList();
        names.ShouldContain(name => name.EndsWith(".trace", StringComparison.Ordinal));
        names.ShouldContain(name => name.EndsWith(".jpeg", StringComparison.Ordinal));
        names.ShouldNotContain(name => name.EndsWith(".network", StringComparison.Ordinal));

        // The screenshots are kept as they were taken, which a JPEG's first bytes show.
        using (var screenshot = archive.Entries.First(entry => entry.FullName.EndsWith(".jpeg", StringComparison.Ordinal)).Open())
        {
            var start = new byte[2];
            screenshot.ReadExactly(start);
            start.ShouldBe(new byte[] { 0xFF, 0xD8 });
        }

        var secrets = await browser.SecretsAsync();
        secrets.ShouldContain(Host.Editor.Password);
        secrets.Count.ShouldBeGreaterThan(3);
        TraceScrubber.Leaks(trace, secrets).ShouldBeNull();
        browser.PolicyViolations.ShouldBeEmpty();
    }

    private static void Write(ZipArchive archive, string name, string text)
    {
        using var stream = archive.CreateEntry(name).Open();
        stream.Write(Encoding.UTF8.GetBytes(text));
    }

    private static string Read(ZipArchiveEntry entry)
    {
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }
}
