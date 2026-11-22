using System.Net;
using Bunit;
using Shouldly;
using Xunit;
using static FluentCMS.Admin.Tests.Shell.FakeManagementServer;

namespace FluentCMS.Admin.Tests.Administration.Plugins;

// The plugins page: each plugin with its contributions, the pages of its admin half and a switch for each site where the
// caller manages plugins; a switch in the shell's site takes the half's contributions out of the shell without a reload.
public sealed class PluginsPageTests : PluginsTestContext
{
    public PluginsPageTests()
    {
        UseNeutralResources();
        Grant(["plugins.manage", "reviews.notes.manage"]);
    }

    [Fact]
    public void EachPluginShowsItsVersionItsContributionsTheLinksToItsHalfAndASwitchPerSite()
    {
        var app = Load("plugins");

        app.WaitForAssertion(() => app.FindAll("main section.cms-card").Count.ShouldBe(2));
        var reviews = Card(app, "reviews");
        var facts = reviews.QuerySelectorAll("dl dd").Select(item => item.TextContent.Trim()).ToArray();
        facts[..7].ShouldBe(["1.2.0", "[1.0.0,2.0.0)", "None", "Enabled", "rating", "reviews.notes.manage", "reviews.digest"]);
        reviews.QuerySelectorAll("ul.plugins-pages a").Select(link => (link.GetAttribute("href"), link.TextContent)).ShouldBe([("reviews", "Reviews"), ("reviews/misinjected", "The page /reviews/misinjected")]);
        Card(app, "search").QuerySelector("details summary")!.TextContent.ShouldBe("1 endpoint");
        Card(app, "search").QuerySelector("ul.plugins-pages").ShouldBeNull();

        Switch(app, "Enabled in Main").Instance.Value.ShouldBeTrue();
        Switch(app, "Enabled in Shop").Instance.Value.ShouldBeTrue();
        app.Find("main a[href='system/plugins']").TextContent.ShouldBe("Admin parts of plugins that this admin did not load");
    }

    [Fact]
    public void APluginTurnedOffInTheShellsSiteTakesItsContributionsOutWithoutAReloadAndBackOnAgain()
    {
        var app = Load("plugins");
        app.WaitForAssertion(() => Links(app).ShouldContain("reviews"));
        var reads = PluginReads;

        Switch(app, "Enabled in Main").Find("fluent-switch").Change(false);

        app.WaitForAssertion(() => Links(app).ShouldNotContain("reviews"));
        Management.Calls.ShouldContain(call => call.Method == HttpMethod.Put && call.Path == $"plugins/reviews/sites/{MainSite:D}" && call.Body == """{"enabled":false}""");
        PluginReads.ShouldBe(reads + 1);
        var reviews = Card(app, "reviews");
        reviews.QuerySelector(".plugins-message")!.TextContent.ShouldBe("reviews is disabled in Main.");
        reviews.TextContent.ShouldContain("Its admin pages show in a site where it is enabled.");
        reviews.QuerySelectorAll("dl dd")[3].TextContent.ShouldBe("Disabled");
        Switch(app, "Enabled in Main").Instance.Value.ShouldBeFalse();
        Navigation.Uri.ShouldEndWith("/plugins");

        Switch(app, "Enabled in Main").Find("fluent-switch").Change(true);

        app.WaitForAssertion(() => Links(app).ShouldContain("reviews"));
        Card(app, "reviews").QuerySelector(".plugins-message")!.TextContent.ShouldBe("reviews is enabled in Main.");
        Card(app, "reviews").QuerySelectorAll("ul.plugins-pages a").Length.ShouldBe(2);
    }

    // The shell keeps the plugins it read before, and the next read, as at a site switch or a reload, has the change.
    [Fact]
    public void AReadOfTheShellsPluginsThatFailsAfterASwitchLeavesTheShellAsItWas()
    {
        var reads = 0;
        Management.Answer = (method, path) => method == HttpMethod.Get && path == "plugins" && ++reads == 3 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : null;
        var app = Load("plugins");
        app.WaitForAssertion(() => Links(app).ShouldContain("reviews"));

        Switch(app, "Enabled in Main").Find("fluent-switch").Change(false);

        app.WaitForAssertion(() => Card(app, "reviews").QuerySelector(".plugins-message")!.TextContent.ShouldBe("reviews is disabled in Main."));
        reads.ShouldBe(3);
        Links(app).ShouldContain("reviews");
        Context.Status.ShouldBe(FluentCMS.Admin.Shell.ShellStatus.Ready);
    }

    // The server may have applied the change, so the shell reads its plugins again once the answer comes, page or no page.
    [Fact]
    public void LeavingThePageWhileASwitchIsUnderWayStillTakesTheContributionsOut()
    {
        var held = new TaskCompletionSource();
        Management.HoldPath = path => path.StartsWith("/api/management/v1/plugins/reviews/sites/", StringComparison.Ordinal) ? held.Task : null;
        var app = Load("plugins");
        app.WaitForAssertion(() => Links(app).ShouldContain("reviews"));
        var reads = PluginReads;

        Switch(app, "Enabled in Main").Find("fluent-switch").Change(false);
        Navigation.NavigateTo("");
        app.WaitForAssertion(() => Shown(app).ShouldBeNull());
        held.SetResult();

        app.WaitForAssertion(() => Links(app).ShouldNotContain("reviews"));
        Disabled.ShouldBe([("reviews", MainSite)]);
        PluginReads.ShouldBe(reads + 1);
    }

    [Fact]
    public void APluginTurnedOffInAnotherSiteLeavesTheShellAsItIs()
    {
        var app = Load("plugins");
        app.WaitForAssertion(() => Links(app).ShouldContain("reviews"));
        var reads = PluginReads;

        Switch(app, "Enabled in Shop").Find("fluent-switch").Change(false);

        app.WaitForAssertion(() => Card(app, "reviews").QuerySelector(".plugins-message")!.TextContent.ShouldBe("reviews is disabled in Shop."));
        Disabled.ShouldBe([("reviews", Shop)]);
        PluginReads.ShouldBe(reads);
        Links(app).ShouldContain("reviews");
        Switch(app, "Enabled in Shop").Instance.Value.ShouldBeFalse();
        Switch(app, "Enabled in Main").Instance.Value.ShouldBeTrue();
    }

    [Fact]
    public void ASwitchTheServerRefusesGoesBackAndSaysWhy()
    {
        Management.Answer = (method, path) => method == HttpMethod.Put ? Problem(HttpStatusCode.Forbidden, "FCMS2001") : null;
        var app = Load("plugins");
        app.WaitForAssertion(() => app.FindAll("main section.cms-card").Count.ShouldBe(2));

        Switch(app, "Enabled in Shop").Find("fluent-switch").Change(false);

        app.WaitForAssertion(() => Card(app, "reviews").QuerySelector(".plugins-message")!.TextContent.ShouldBe("Not changed: you may no longer enable or disable plugins in Shop."));
        Switch(app, "Enabled in Shop").Instance.Value.ShouldBeTrue();
        Switch(app, "Enabled in Shop").Instance.Disabled.ShouldBeFalse();
        Disabled.ShouldBeEmpty();
    }

    [Fact]
    public void ASiteWhereTheCallerManagesNoPluginHasNoSwitch()
    {
        Managed.Remove(Shop);

        var app = Load("plugins");

        app.WaitForAssertion(() => app.FindAll("main section.cms-card").Count.ShouldBe(2));
        app.FindComponents<FluentCMS.Admin.Components.CmsSwitch>().Select(toggle => toggle.Instance.Label).ShouldBe(["Enabled in Main", "Enabled in Main"]);
    }

    [Fact]
    public void AListThatCannotBeReadOffersToTryAgain()
    {
        // The shell's own read comes first; the page's is the second.
        var reads = 0;
        Management.Answer = (method, path) => path == "plugins" && ++reads == 2 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : null;
        var app = Load("plugins");

        app.WaitForAssertion(() => app.Find("main [role=alert]").TextContent.ShouldStartWith("The plugins could not be read."));
        app.Find("main [role=alert] fluent-button").Click();

        app.WaitForAssertion(() => app.FindAll("main section.cms-card").Count.ShouldBe(2));
    }
}
