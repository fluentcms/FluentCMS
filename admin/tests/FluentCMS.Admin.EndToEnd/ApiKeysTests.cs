using System.Collections.Concurrent;
using System.Net;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// The API keys pages (spec 4.15) with the secret shown once (AQ23): an administrator makes a key for one site and one role,
// copies its secret, reads content with it and revokes it. The secret is never shown on the page, only copied, so no
// screenshot of the trace can hold it; the trace is kept and checked for it as text, and the secret is in no storage, no
// address and no console message.
public sealed class ApiKeysTests(EndToEndHost host) : BrowserTest(host)
{
    [Fact]
    public async Task AnAdministratorMakesAKeyCopiesItsSecretOnceReadsContentWithItAndRevokesIt()
    {
        var name = $"Feed {Mark}";
        var traces = Host.NewDirectory();
        var browser = await OpenBrowserAsync(traces: traces.FullName);
        await browser.GrantClipboardAsync();
        var tab = await SignedInTabAsync(browser, "api-keys/new", await Host.CreateEditorAsync("Super Admin"));
        var console = new ConcurrentQueue<string>();
        tab.Page.Console += (_, message) => console.Enqueue(message.Text);
        var main = tab.Page.Locator("main");
        await Expect(main.Locator("h1")).ToHaveTextAsync(Text("ApiKeys.New.Heading"));

        await tab.Page.GetByLabel(Text("ApiKeys.New.Name")).FillAsync(name);
        var role = tab.Page.GetByLabel(Text("ApiKeys.New.Role"));
        await Expect(role.Locator("option", new LocatorLocatorOptions { HasText = "Editor" })).ToHaveCountAsync(1);
        await role.SelectOptionAsync(new SelectOptionValue { Label = "Editor" });
        var dialog = await tab.OpenDialogAsync(() => Ui.ButtonIn(main, Text("ApiKeys.New.Create")).ClickAsync(), Text("ApiKeys.Secret.CreatedTitle"));

        await Ui.ButtonIn(dialog, Repository.ComponentText("SecretCopy")).ClickAsync();
        await Expect(dialog.GetByText(Repository.ComponentText("SecretCopied"), new LocatorGetByTextOptions { Exact = true })).ToBeVisibleAsync();
        var secret = await tab.Page.EvaluateAsync<string>("() => navigator.clipboard.readText()");
        secret.ShouldStartWith("fcms_dk_");

        // Given to the mask here: Chromium does not always keep the body of the answer that made the key for the harness to
        // read (#3635). The page shows it nowhere.
        browser.Mask(secret);
        (await tab.Page.EvaluateAsync<bool>("secret => document.documentElement.outerHTML.includes(secret)", secret)).ShouldBeFalse();

        await Ui.ButtonIn(dialog, Repository.ComponentText("SecretDone")).ClickAsync();
        await Expect(main.Locator("h1")).ToHaveTextAsync(name);
        await Expect(tab.ShownDialog(Text("ApiKeys.Secret.CreatedTitle"))).ToHaveCountAsync(0);
        (await tab.Page.EvaluateAsync<bool>(
            """
            secret => document.documentElement.outerHTML.includes(secret) || location.href.includes(secret)
                || [localStorage, sessionStorage].some(storage => Object.keys(storage).some(key => (storage.getItem(key) ?? '').includes(secret)))
            """,
            secret)).ShouldBeFalse();

        // The key reads the site's content, until it is revoked.
        (await ReadContentAsync(secret)).ShouldBe(HttpStatusCode.OK);
        await tab.OpenDialogAsync(() => Ui.ButtonIn(main, Text("ApiKeys.Key.Revoke")).ClickAsync(), Ui.Format(Text("ApiKeys.Confirm.RevokeTitle"), name));
        await Ui.ButtonIn(tab.Dialog(Ui.Format(Text("ApiKeys.Confirm.RevokeTitle"), name)), Text("ApiKeys.Confirm.Revoke")).ClickAsync();
        await Expect(main.GetByText(Text("ApiKeys.Key.RevokedNote"), new LocatorGetByTextOptions { Exact = true })).ToBeVisibleAsync();
        (await ReadContentAsync(secret)).ShouldBe(HttpStatusCode.Unauthorized);

        await tab.Sidebar.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = Repository.AdminText("Administration.Menu.ApiKeys"), Exact = true }).ClickAsync();
        await Expect(main.Locator("h1")).ToHaveTextAsync(Repository.AdministrationHeading("ApiKeys"));
        var row = main.Locator("tr, [role=row]").Filter(new LocatorFilterOptions { HasText = name });
        await Expect(row).ToContainTextAsync(Ui.Format(Text("ApiKeys.Status.Revoked"), "").Trim());

        console.ShouldAllBe(message => !message.Contains(secret, StringComparison.Ordinal));
        var trace = Path.Combine(traces.FullName, "api-keys.zip");
        await browser.SaveTraceAsync(trace);
        File.Exists(trace).ShouldBeTrue();
        TraceScrubber.Leaks(trace, [secret]).ShouldBeNull();
        browser.PolicyViolations.ShouldBeEmpty();
    }

    private static string Text(string key) => Repository.AdministrationText("ApiKeys", key);

    private async Task<HttpStatusCode> ReadContentAsync(string secret)
    {
        using var client = Host.Client();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("api/delivery/v1/article", UriKind.Relative));
        request.Headers.Add("X-Api-Key", secret);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }
}
