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

    // Each kind of secret the context collects, in an entry the mask does not rewrite, keeps the trace from being kept at all,
    // and a trace already at the path stays as it was.
    [Theory]
    [InlineData(Password)]
    [InlineData(Cookie)]
    [InlineData("eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJl")]
    [InlineData("eyJhbGciOiJSU0EtT0FFUCIsImVuYyI6IkEyNTZDQkMtSFM1MTIifQ.a.b.c.d")]
    [InlineData("verifier-0123456789abcdefghij")]
    [InlineData("CfDJ8AbCdEfGhIjKlMnOpQrStUvWxYz0123456789")]
    public void ATraceThatStillHoldsASecretIsNotKept(string secret)
    {
        var source = Path.Combine(_directory.FullName, "source.zip");
        using (var archive = ZipFile.Open(source, ZipArchiveMode.Create))
        {
            Write(archive, "trace.trace", "{}");
            Write(archive, "screencast/page@1.jpeg", "jpeg " + secret);
        }

        var masked = Path.Combine(_directory.FullName, "masked.zip");
        File.WriteAllText(masked, "earlier");
        Should.Throw<InvalidOperationException>(() => TraceScrubber.Scrub(source, masked, [secret]));
        File.ReadAllText(masked).ShouldBe("earlier");
        File.Exists(source + ".masked").ShouldBeFalse();
    }

    // A secret holding a space, a quote and an exclamation mark, as JavaScript, JSON and a browser's form write it.
    [Fact]
    public void TheMaskFindsASecretAsJavaScriptJsonAndAFormWriteIt()
    {
        const string secret = "Aa1! with \"quote\"";
        var source = Path.Combine(_directory.FullName, "source.zip");
        using (var archive = ZipFile.Open(source, ZipArchiveMode.Create))
        {
            Write(archive, "trace.trace", "fill(\"Aa1! with \\\"quote\\\"\")\n{\"value\":\"Aa1! with \\u0022quote\\u0022\"}\nInput.Secret=Aa1%21+with+%22quote%22");
        }

        var masked = Path.Combine(_directory.FullName, "masked.zip");
        TraceScrubber.Scrub(source, masked, [secret]);

        using var result = ZipFile.OpenRead(masked);
        var trace = Read(result.GetEntry("trace.trace")!);
        trace.ShouldNotContain("quote");
        trace.ShouldContain("[masked]");
    }

    // The chunks recorded around an unrecorded scope are one trace: each event log under a name of its own, which the trace
    // viewer reads as one more context, a resource both hold once, and every chunk masked and checked.
    [Fact]
    public void TheChunksOfATraceAreJoinedWithAnEventLogEachAndEveryResourceOnce()
    {
        var first = Path.Combine(_directory.FullName, "chunk-0.zip");
        using (var archive = ZipFile.Open(first, ZipArchiveMode.Create))
        {
            Write(archive, "trace.trace", """{"type":"context-options","title":"first"}""");
            Write(archive, "trace.network", "{}");
            Write(archive, "trace.stacks", "{}");
            Write(archive, "resources/4e5f.css", "body { color: red }");
            Write(archive, "resources/page@1-1.jpeg", "jpeg");
        }

        var second = Path.Combine(_directory.FullName, "chunk-1.zip");
        using (var archive = ZipFile.Open(second, ZipArchiveMode.Create))
        {
            Write(archive, "trace.trace", "{\"type\":\"context-options\",\"title\":\"second\"}\n{\"type\":\"after\",\"value\":\"" + Cookie + "\"}");
            Write(archive, "trace.network", "{}");
            Write(archive, "resources/4e5f.css", "body { color: red }");
            Write(archive, "resources/page@1-2.jpeg", "jpeg");
        }

        var masked = Path.Combine(_directory.FullName, "masked.zip");
        TraceScrubber.Scrub([first, second], masked, [Cookie]);

        using var result = ZipFile.OpenRead(masked);
        result.Entries.Select(entry => entry.FullName).ShouldBe(
            ["trace.trace", "trace.stacks", "resources/4e5f.css", "resources/page@1-1.jpeg", "chunk1-trace.trace", "resources/page@1-2.jpeg"],
            ignoreOrder: true);
        Read(result.GetEntry("trace.trace")!).ShouldContain("first");
        var later = Read(result.GetEntry("chunk1-trace.trace")!);
        later.ShouldContain("second");
        later.ShouldNotContain(Cookie);
        TraceScrubber.Leaks(masked, [Cookie]).ShouldBeNull();
    }

    // A secret the case gives, which no pattern finds, is masked in the snapshots around an unrecorded scope, and the steps of
    // the scope, which show another, leave no action, snapshot or screenshot in the kept trace. A value too short to mask is
    // refused.
    [Fact]
    public async Task AnUnrecordedScopeLeavesNoStepInTheTraceAndAGivenSecretIsMaskedAroundIt()
    {
        var trace = Path.Combine(_directory.FullName, "unrecorded.zip");
        var given = "given-" + Guid.NewGuid().ToString("N");
        var unrecorded = "unrecorded-" + Guid.NewGuid().ToString("N");
        var browser = await OpenBrowserAsync(traces: _directory.FullName);
        var tab = await SignedInTabAsync(browser, "");
        await Expect(tab.Sidebar).ToBeVisibleAsync();
        Should.Throw<ArgumentOutOfRangeException>(() => browser.Mask("1234567"));

        browser.Mask(given);
        await ShowAsync(tab, "e2e-given", given);
        await browser.UnrecordedAsync(async () =>
        {
            await ShowAsync(tab, "e2e-unrecorded", unrecorded);
            await tab.Page.EvaluateAsync("() => document.getElementById('e2e-unrecorded').remove()");
            await Expect(tab.Page.Locator("#e2e-unrecorded")).ToHaveCountAsync(0);
        });

        await tab.Page.Locator("#e2e-given").ClickAsync();
        await browser.SaveTraceAsync(trace);

        using var archive = ZipFile.OpenRead(trace);
        archive.Entries.Select(entry => entry.FullName).Where(name => name.EndsWith(".trace", StringComparison.Ordinal)).ShouldBe(["trace.trace", "chunk1-trace.trace"], ignoreOrder: true);
        archive.Entries.Count(entry => entry.FullName.EndsWith(".jpeg", StringComparison.Ordinal)).ShouldBeGreaterThan(0);
        var texts = archive.Entries.Select(entry => Read(entry)).ToList();
        texts.ShouldContain(text => text.Contains("e2e-given", StringComparison.Ordinal), "the snapshots around the scope were not kept");
        texts.ShouldNotContain(text => text.Contains("e2e-unrecorded", StringComparison.Ordinal), "a step of the unrecorded scope is in the trace");
        TraceScrubber.Leaks(trace, [.. await browser.SecretsAsync(), unrecorded]).ShouldBeNull();
    }

    // Steps that fail may leave their secret on the page, so nothing is recorded after them: the trace a failed case keeps
    // holds the chunk recorded before them alone.
    [Fact]
    public async Task UnrecordedStepsThatFailRecordNothingAgainAndTheKeptTraceEndsBeforeThem()
    {
        var trace = Path.Combine(_directory.FullName, "failed.zip");
        var shown = "shown-" + Guid.NewGuid().ToString("N");
        var browser = await OpenBrowserAsync(traces: _directory.FullName);
        var tab = await SignedInTabAsync(browser, "");
        await Expect(tab.Sidebar).ToBeVisibleAsync();

        await Should.ThrowAsync<InvalidOperationException>(() => browser.UnrecordedAsync(async () =>
        {
            await ShowAsync(tab, "e2e-failed", shown);
            throw new InvalidOperationException("The steps failed with their secret on the page.");
        }));

        await tab.Page.Locator("#e2e-failed").ClickAsync();
        await browser.SaveTraceAsync(trace);

        using var archive = ZipFile.OpenRead(trace);
        archive.Entries.Select(entry => entry.FullName).Where(name => name.EndsWith(".trace", StringComparison.Ordinal)).ShouldBe(["trace.trace"]);
        archive.Entries.Select(entry => Read(entry)).ShouldNotContain(text => text.Contains("e2e-failed", StringComparison.Ordinal), "a step during or after the failed steps is in the trace");
        TraceScrubber.Leaks(trace, [.. await browser.SecretsAsync(), shown]).ShouldBeNull();
    }

    // A paragraph with the text over the page, clicked, so that its action has snapshots and a screenshot shows it.
    private static async Task ShowAsync(AdminTab tab, string id, string text)
    {
        await tab.Page.EvaluateAsync(
            "([id, text]) => { const shown = document.createElement('p'); shown.id = id; shown.textContent = text; shown.style.cssText = 'position: fixed; inset: 0 auto auto 0; z-index: 2147483647; margin: 0; background: white'; document.body.append(shown); }",
            new[] { id, text });
        await tab.Page.Locator("#" + id).ClickAsync();
    }

    // The trace of a real sign-in, saved as a failed test saves it: screenshots and snapshots, and nothing of the network.
    [Fact]
    public async Task TheTraceOfASignInHoldsNoPasswordOrToken()
    {
        var trace = Path.Combine(_directory.FullName, "sign-in.zip");
        var editor = await EditorAsync();
        var browser = await OpenBrowserAsync(traces: _directory.FullName);
        var tab = await SignedInTabAsync(browser, "", editor);
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

        // The context saw the tokens of the sign-in and of the reload in the token endpoint's answers, not only the password.
        var secrets = await browser.SecretsAsync();
        secrets.Any(secret => secret.StartsWith("eyJ", StringComparison.Ordinal)).ShouldBeTrue();
        secrets.Count(secret => secret != editor.Password).ShouldBeGreaterThan(3);
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
