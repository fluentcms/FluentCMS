using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Bunit;
using FluentCMS.Admin.Localization;
using FluentCMS.Admin.Tests.Shell;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests;

public sealed class LocalizationTests : AdminTestContext
{
    // Every routable page of the admin, from its [Route] attributes, and an address that has none.
    public static TheoryData<string, string> Routes()
    {
        var data = new TheoryData<string, string>();
        foreach (var culture in new[] { PseudoCulture.LeftToRight, PseudoCulture.RightToLeft })
        {
            foreach (var route in RoutablePages().SelectMany(page => page.GetCustomAttributes<RouteAttribute>()).Select(route => route.Template))
            {
                data.Add(culture, route);
            }

            data.Add(culture, "/no-such-page");
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public void EveryTextOfEveryPageComesFromTheResources(string culture, string route)
    {
        UseInterfaceCulture(culture);
        Start();
        Services.GetRequiredService<NavigationManager>().NavigateTo(route);

        var app = Render<AdminApp>();

        // FocusOnNavigate does nothing on a page without a heading.
        app.FindAll("h1").ShouldHaveSingleItem();
        PseudoCulture.TextCount(app.Nodes).ShouldBeGreaterThan(1);
        PseudoCulture.Unlocalized(app.Nodes).ShouldBeEmpty();
    }

    // The shell in each state that shows texts of its own. The data the fake serves, site and type names, is marked as a
    // resource is, so that only the shell's own texts are checked.
    public static TheoryData<string, string> ShellStates()
    {
        var data = new TheoryData<string, string>();
        foreach (var culture in new[] { PseudoCulture.LeftToRight, PseudoCulture.RightToLeft })
        {
            foreach (var state in new[] { "Sites", "NoSite", "NoSchema", "Failed", "Loading" })
            {
                data.Add(culture, state);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ShellStates))]
    public void EveryTextOfTheShellComesFromTheResources(string culture, string state)
    {
        UseInterfaceCulture(culture);
        var rightToLeft = culture == PseudoCulture.RightToLeft;
        var shop = new Guid("0193a1b2-0000-7000-8000-00000000a002");
        Management.Sites[0]!["name"] = PseudoCulture.Transform("Main", rightToLeft);
        Management.AddSite(shop, PseudoCulture.Transform("Shop", rightToLeft), "shop");
        Management.SetTypes(
            FakeManagementServer.MainSite,
            FakeManagementServer.Type("article", displayName: PseudoCulture.Transform("Articles", rightToLeft)),
            FakeManagementServer.Type("tag", scope: "global", displayName: PseudoCulture.Transform("Tags", rightToLeft)),
            FakeManagementServer.Type("settings", "singleton", displayName: PseudoCulture.Transform("Settings", rightToLeft)));
        switch (state)
        {
            case "NoSite":
                Management.Sites.Clear();
                break;
            case "NoSchema":
                Management.Grant(FakeManagementServer.MainSite, []);
                break;
            case "Failed":
                Management.Unavailable = true;
                break;
            case "Loading":
                Management.Hold = new TaskCompletionSource();
                break;
        }

        Start();

        var app = Render<AdminApp>();

        var shown = state switch
        {
            "Sites" => ".site-switcher select, .cms-nav-section-title, .cms-badge",
            "NoSite" => "main h1",
            "NoSchema" => "main .shell-notice",
            "Failed" => "main h1, main fluent-button",
            _ => "main [role=status]",
        };
        app.WaitForAssertion(() => shown.Split(", ").ShouldAllBe(selector => app.FindAll(selector).Count > 0));
        PseudoCulture.Unlocalized(app.Nodes).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(PseudoCulture.LeftToRight)]
    [InlineData(PseudoCulture.RightToLeft)]
    public void EveryTextOfTheConfigurationErrorPageComesFromTheResources(string culture)
    {
        UseInterfaceCulture(culture);
        Start("""{ "apiBaseUrl": "cms.example.com", "clientId": "a b", "branding": { "accentColor": "red" }, "cultures": { "offered": [] } }""");
        var head = Render<HeadOutlet>();

        var app = Render<AdminApp>();

        app.FindAll("li").Count.ShouldBe(4);
        PseudoCulture.Unlocalized(app.Nodes).ShouldBeEmpty();
        PseudoCulture.Unlocalized(head.Nodes).ShouldBeEmpty();
        head.Find("title").TextContent.ShouldBe(Text("Configuration.Error.Title"));
    }

    [Fact]
    public void EveryRoutablePageIsKeptFromTheTrimmer()
    {
        var kept = typeof(AdminApp).GetConstructor(Type.EmptyTypes)!
            .GetCustomAttributes<DynamicDependencyAttribute>()
            .Select(dependency => dependency.Type);

        kept.ShouldBe(RoutablePages(), ignoreOrder: true);
    }

    [Fact]
    public void TheCheckFailsOnAHardCodedString()
    {
        var page = Render(builder => builder.AddMarkupContent(0, """
            <p>Saved</p>
            <button aria-label="Close"></button>
            <p>[Draft]</p>
            <p>⟦Showing⟧ 1 to 10 of 50 ⟦entries⟧</p>
            <input type="submit" value="Send" />
            <div role="slider" aria-valuetext="High"></div>
            <section aria-roledescription="Carousel"></section>
            <optgroup label="Colors"></optgroup>
            """));

        PseudoCulture.Unlocalized(page.Nodes).ShouldBe(
        [
            "Saved", "aria-label=\"Close\"", "[Draft]", "⟦Showing⟧ 1 to 10 of 50 ⟦entries⟧", "value=\"Send\"",
            "aria-valuetext=\"High\"", "aria-roledescription=\"Carousel\"", "label=\"Colors\"",
        ]);
    }

    [Fact]
    public void TheCheckSkipsTheCodeOfStyleAndScriptElements()
    {
        var page = Render(builder => builder.AddMarkupContent(0, """<style>#a::part(dialog) { width: 100%; }</style><script>let a;</script><p>Saved</p>"""));

        PseudoCulture.Unlocalized(page.Nodes).ShouldBe(["Saved"]);
    }

    [Fact]
    public void TheCheckPassesNumbersAndPunctuationBetweenResources()
    {
        var page = Render(builder => builder.AddMarkupContent(0, """<p>⟦Showing⟧ 1–10 / 50</p><input type="text" value="data" />"""));

        PseudoCulture.Unlocalized(page.Nodes).ShouldBeEmpty();
    }

    [Fact]
    public void TheCheckFailsOnAKeyMissingFromTheResources()
    {
        Start();

        var page = Render(builder => builder.AddContent(0, Text("Shell.Nowhere.Missing")));

        PseudoCulture.Unlocalized(page.Nodes).ShouldBe(["Shell.Nowhere.Missing"]);
    }

    [Theory]
    [InlineData(PseudoCulture.LeftToRight, "ltr")]
    [InlineData(PseudoCulture.RightToLeft, "rtl")]
    public void TheDocumentTakesTheLanguageAndDirectionOfTheInterface(string culture, string direction)
    {
        UseInterfaceCulture(culture);
        Start();

        Render<AdminApp>();

        Document.VerifyInvoke("setLanguage").Arguments.ShouldBe([culture, direction]);
    }

    [Theory]
    [InlineData(PseudoCulture.RightToLeft, "rtl")]
    [InlineData(PseudoCulture.LeftToRight, "ltr")]
    public void TheErrorPageAlsoSetsTheLanguageAndDirection(string culture, string direction)
    {
        UseInterfaceCulture(culture);
        Start("{}");

        Render<AdminApp>();

        Document.VerifyInvoke("setLanguage").Arguments.ShouldBe([culture, direction]);
    }

    [Theory]
    [InlineData(PseudoCulture.LeftToRight, "ar", "rtl")]
    [InlineData(PseudoCulture.LeftToRight, "en", "ltr")]
    [InlineData(PseudoCulture.RightToLeft, "en", "ltr")]
    [InlineData(PseudoCulture.RightToLeft, "fa-IR", "rtl")]
    public void AFieldTakesTheDirectionOfTheContentLocaleNotOfTheInterface(string culture, string locale, string direction)
    {
        UseInterfaceCulture(culture);
        Start();

        var field = Render<ContentLocale>(parameters => parameters
            .Add(p => p.Locale, locale)
            .AddChildContent("<input />"));

        var scope = field.Find("div");
        scope.GetAttribute("dir").ShouldBe(direction);
        scope.GetAttribute("lang").ShouldBe(locale);
        scope.QuerySelector("input").ShouldNotBeNull();
    }

    // Decided by the script, then the language, since the culture data of WebAssembly lacks most right-to-left locales.
    [Theory]
    [InlineData("ar", "rtl")]
    [InlineData("ar-SA", "rtl")]
    [InlineData("he", "rtl")]
    [InlineData("iw", "rtl")]
    [InlineData("fa", "rtl")]
    [InlineData("ur", "rtl")]
    [InlineData("ur-PK", "rtl")]
    [InlineData("ps", "rtl")]
    [InlineData("yi", "rtl")]
    [InlineData("ckb", "rtl")]
    [InlineData("dv", "rtl")]
    [InlineData("az-Arab", "rtl")]
    [InlineData("pa-Arab-PK", "rtl")]
    [InlineData("ar-Latn", "ltr")]
    [InlineData("sr-Latn", "ltr")]
    [InlineData("sr-Cyrl-RS", "ltr")]
    [InlineData("az", "ltr")]
    [InlineData("en-US", "ltr")]
    [InlineData("zh-Hant-TW", "ltr")]
    [InlineData(PseudoCulture.RightToLeft, "rtl")]
    [InlineData(PseudoCulture.LeftToRight, "ltr")]
    [InlineData("not a locale!", "ltr")]
    public void TheDirectionOfALanguageTag(string tag, string direction)
    {
        TextDirection.Of(tag).ShouldBe(direction);
    }

    private static List<Type> RoutablePages() =>
        typeof(AdminApp).Assembly.GetTypes().Where(type => type.IsDefined(typeof(RouteAttribute))).ToList();
}
