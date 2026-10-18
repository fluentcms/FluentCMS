using Bunit;
using FluentCMS.Admin.Components.Layout;

namespace FluentCMS.Tests.Admin;

public sealed class ThemeToggleTests
{
    [Fact]
    public void Renders_a_named_menu_button_that_offers_light_dark_and_the_system_theme()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<ThemeToggle>();

        var trigger = cut.Find("[data-testid=theme-toggle]");
        var menu = cut.Find("[role=menu]");
        var items = cut.FindAll("[role=menuitem]");

        Assert.Equal("BUTTON", trigger.TagName);
        Assert.Equal(menu.Id, trigger.GetAttribute("popovertarget"));
        Assert.Equal("menu", trigger.GetAttribute("aria-haspopup"));
        Assert.Equal("Toggle theme", trigger.QuerySelector(".sr-only")!.TextContent);
        Assert.All(trigger.QuerySelectorAll("svg"), icon => Assert.Equal("true", icon.GetAttribute("aria-hidden")));
        Assert.Equal(["light", "dark", "system"], items.Select(item => item.GetAttribute("data-theme-choice")));
        Assert.Equal(["Light", "Dark", "System"], items.Select(item => item.TextContent.Trim()));
    }

    [Fact]
    public void The_choice_is_left_to_admin_js_with_no_inline_script_or_style()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<ThemeToggle>();

        // admin.js applies data-theme-choice, so the toggle works from the static layout and while the circuit is down.
        Assert.All(cut.FindAll("[data-theme-choice]"), item => Assert.Equal("-1", item.GetAttribute("tabindex")));
        Assert.Empty(cut.FindAll("[style], script, style"));
        Assert.DoesNotContain(cut.FindAll("*").SelectMany(element => element.Attributes), attribute => attribute.Name.StartsWith("on", StringComparison.Ordinal));
    }

    [Fact]
    public void Takes_extra_classes_for_its_place_in_the_header()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<ThemeToggle>(toggle => toggle.Add(t => t.Class, "ms-auto"));

        Assert.Contains("ms-auto", cut.Find("[data-testid=theme-toggle]").ClassList);
    }
}
