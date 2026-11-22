using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace FluentCMS.Admin.EndToEnd;

// The settings menu and the generated settings page of an area (spec 4.16): an administrator changes a setting of the
// site, sees it as the site's own after a reload, resets it and sees the default again. The Blog sample's area is used,
// whose value the case marks as its own.
public sealed class SettingsTests(EndToEndHost host) : BrowserTest(host)
{
    [Fact]
    public async Task AnAdministratorChangesASettingOfTheSiteSeesItAsTheSitesOwnResetsItAndSeesTheDefaultAgain()
    {
        var name = $"Blog {Mark}";
        var browser = await OpenBrowserAsync();
        var tab = await SignedInTabAsync(browser, "", await Host.CreateEditorAsync("Super Admin"));
        var main = tab.Page.Locator("main");
        await tab.Sidebar.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = Repository.AdminText("Administration.Menu.Settings"), Exact = true }).ClickAsync();
        await Expect(main.GetByRole(AriaRole.Heading, new LocatorGetByRoleOptions { Name = Repository.AdministrationHeading("Settings"), Exact = true })).ToBeVisibleAsync();

        await main.GetByRole(AriaRole.Link, new LocatorGetByRoleOptions { Name = "Blog", Exact = true }).ClickAsync();
        await Expect(main.GetByRole(AriaRole.Heading, new LocatorGetByRoleOptions { Name = "Blog", Exact = true })).ToBeVisibleAsync();
        // The label of a required value ends with its marker.
        var siteName = main.GetByLabel(new Regex(@"^Site name\b"));
        var source = main.Locator(".settings-source");
        await Expect(source).ToBeVisibleAsync();

        // Typed and saved from the keyboard alone.
        await siteName.FillAsync(name);
        var saved = await tab.AnswerAsync(() => Ui.PressAsync(Ui.ButtonIn(main, Text("Settings.Form.Save")), "Enter"), "PUT", tab.ManagementUrl("settings/blog"));
        saved.Status.ShouldBe(200);
        await Expect(source).ToHaveTextAsync(Text("Settings.Source.Site"));

        await tab.ReloadAsync();
        await Expect(siteName).ToHaveValueAsync(name);
        await Expect(source).ToHaveAttributeAsync("data-source", "site");

        var dialog = await tab.OpenDialogAsync(() => Ui.ButtonIn(main, Text("Settings.Reset.Site")).ClickAsync(), Text("Settings.Reset.Title"));
        var reset = await tab.AnswerAsync(() => Ui.ButtonIn(dialog, Text("Settings.Reset.Confirm")).ClickAsync(), "DELETE", tab.ManagementUrl("settings/blog"));
        reset.Status.ShouldBe(204);

        // The site follows the defaults of the instance again, or the code's when the instance sets none.
        await Expect(source).ToHaveAttributeAsync("data-source", new Regex("^(default|code)$"));
        await Expect(siteName).Not.ToHaveValueAsync(name);
        await Expect(main.Locator(".settings-message")).ToHaveTextAsync(Text("Settings.Reset.Done"));
        browser.PolicyViolations.ShouldBeEmpty();
    }

    private static string Text(string key) => Repository.AdministrationText("Settings", key);
}
