using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// The MCP half (spec 5.4) on the reference host, by keyboard: a client that registered itself is approved with a role from
// the clients page, and the guidelines of the site are written on the instructions page, which the sidebar leads to. The
// case removes the client and the guidelines, so that the other classes see neither.
public sealed class McpTests(EndToEndHost host) : BrowserTest(host)
{
    private static readonly Dictionary<string, string> McpTexts =
        XDocument.Load(Path.Combine(Repository.Root, "admin", "src", "plugins", "FluentCMS.Plugins.Mcp.Admin", "McpResources.resx")).Root!.Elements("data")
            .ToDictionary(data => (string)data.Attribute("name")!, data => (string)data.Element("value")!, StringComparer.Ordinal);

    [Fact]
    public async Task AnAdministratorApprovesAClientThatRegisteredItselfAndWritesTheGuidelinesOfTheSite()
    {
        var name = $"Agent {Mark}";
        string? clientId = null;
        var wroteGuidelines = false;
        using var management = await Host.ManagementClientAsync();
        try
        {
            using (var agent = Host.Client())
            {
                using var body = new StringContent(
                    $$"""{ "client_name": "{{name}}", "redirect_uris": ["http://127.0.0.1:41000/callback"], "scope": "openid mcp" }""",
                    Encoding.UTF8,
                    "application/json");
                using var registered = await agent.PostAsync(new Uri("connect/register", UriKind.Relative), body, TestContext.Current.CancellationToken);
                registered.StatusCode.ShouldBe(HttpStatusCode.Created);
            }

            var browser = await OpenBrowserAsync();
            var tab = await SignedInTabAsync(browser, "mcp/clients", await Host.CreateEditorAsync("Super Admin"));
            var main = tab.Page.Locator("main");
            await Expect(Heading(tab, Mcp("Mcp.Clients.Page.Heading"))).ToBeVisibleAsync();
            var row = main.Locator("table.mcp-clients tbody tr").Filter(new LocatorFilterOptions { HasText = name });
            await Expect(row).ToContainTextAsync(Mcp("Mcp.Clients.Status.pending"));

            var dialog = await tab.OpenDialogAsync(
                () => Ui.PressAsync(Ui.ButtonIn(row, Mcp("Mcp.Clients.Approve") + Ui.Format(Mcp("Mcp.Clients.Which"), name)), "Enter"),
                Mcp("Mcp.Role.ApproveTitle"));
            await dialog.GetByLabel(new Regex($@"^{Regex.Escape(Mcp("Mcp.Role.Role"))}\b")).SelectOptionAsync(new SelectOptionValue { Label = "Editor" });
            var approved = await tab.AnswerAsync(
                () => Ui.PressAsync(Ui.ButtonIn(dialog, Mcp("Mcp.Role.Approve")), "Enter"),
                response => response.Request.Method == "POST" && response.Url.StartsWith(tab.ManagementUrl("mcp/clients/"), StringComparison.Ordinal) && response.Url.EndsWith("/approve", StringComparison.Ordinal));
            approved.Status.ShouldBe(200);
            clientId = Regex.Match(approved.Url, "mcp/clients/(?<id>[0-9a-f-]{36})/approve").Groups["id"].Value;
            await Expect(row).ToContainTextAsync(Mcp("Mcp.Clients.Status.approved"));
            await Expect(row).ToContainTextAsync("Editor");
            await Expect(tab.Announced(Ui.Format(Mcp("Mcp.Clients.Approved"), name, "Editor"))).ToBeAttachedAsync();

            await Ui.PressAsync(tab.Sidebar.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = Mcp("Mcp.Instructions.Menu"), Exact = true }), "Enter");
            await Expect(Heading(tab, Mcp("Mcp.Instructions.Page.Heading"))).ToBeVisibleAsync();
            await main.GetByRole(AriaRole.Textbox, new LocatorGetByRoleOptions { Name = Mcp("Mcp.Instructions.Guidelines"), Exact = true }).FillAsync($"Write in plain English. {Mark}");
            var saved = await tab.AnswerAsync(
                () => Ui.PressAsync(Ui.ButtonIn(main, Mcp("Mcp.Instructions.Save") + Mcp("Mcp.Instructions.OfGuidelines")), "Enter"),
                "PUT",
                tab.ManagementUrl("mcp/instructions"));
            saved.Status.ShouldBe(200);
            wroteGuidelines = true;
            await Expect(Ui.ButtonIn(main, Mcp("Mcp.Instructions.Remove") + Mcp("Mcp.Instructions.OfGuidelines"))).ToHaveCountAsync(1);
            await Expect(tab.Announced(Mcp("Mcp.Instructions.Saved"))).ToBeAttachedAsync();
            browser.PolicyViolations.ShouldBeEmpty();
        }
        finally
        {
            // After the case's own removal these are 4xx.
            if (clientId is not null)
            {
                using var revoked = await management.DeleteAsync(new Uri($"api/management/v1/mcp/clients/{clientId}", UriKind.Relative), CancellationToken.None);
            }

            if (wroteGuidelines)
            {
                using var removed = await management.DeleteAsync(new Uri("api/management/v1/mcp/instructions", UriKind.Relative), CancellationToken.None);
            }
        }
    }

    private static string Mcp(string key) =>
        McpTexts.TryGetValue(key, out var text) ? text : throw new KeyNotFoundException($"McpResources.resx has no key {key}.");

    private static ILocator Heading(AdminTab tab, string name) =>
        tab.Page.Locator("main").GetByRole(AriaRole.Heading, new LocatorGetByRoleOptions { Name = name, Exact = true, Level = 1 });
}
