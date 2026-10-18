using Bunit;
using FluentCMS.Admin.Components.Ui;

namespace FluentCMS.Tests.Admin;

public sealed class TabsTests
{
    private static IRenderedComponent<Tabs> RenderTabs(AdminTestContext context) => context.Render<Tabs>(tabs => tabs
        .Add(t => t.DefaultValue, "account")
        .AddChildContent<TabsList>(list => list
            .AddChildContent<TabsTrigger>(trigger => trigger.Add(t => t.Value, "account").AddChildContent("Account"))
            .AddChildContent<TabsTrigger>(trigger => trigger.Add(t => t.Value, "password").AddChildContent("Password"))
            .AddChildContent<TabsTrigger>(trigger => trigger.Add(t => t.Value, "billing").Add(t => t.Disabled, true).AddChildContent("Billing")))
        .AddChildContent<TabsContent>(content => content.Add(c => c.Value, "account").AddChildContent("Account panel"))
        .AddChildContent<TabsContent>(content => content.Add(c => c.Value, "password").AddChildContent("Password panel"))
        .AddChildContent<TabsContent>(content => content.Add(c => c.Value, "billing").AddChildContent("Billing panel")));

    [Fact]
    public void Renders_a_tablist_whose_tabs_control_their_panels()
    {
        using var context = new AdminTestContext();

        var cut = RenderTabs(context);
        var tabs = cut.FindAll("[role=tab]");
        var panels = cut.FindAll("[role=tabpanel]");

        Assert.Equal("horizontal", cut.Find("[role=tablist]").GetAttribute("aria-orientation"));
        Assert.Contains("bg-muted", cut.Find("[role=tablist]").ClassList);
        Assert.Equal(3, tabs.Count);
        Assert.All(tabs, tab => Assert.Equal("BUTTON", tab.TagName));
        Assert.All(tabs.Zip(panels), pair =>
        {
            Assert.Equal(pair.Second.Id, pair.First.GetAttribute("aria-controls"));
            Assert.Equal(pair.First.Id, pair.Second.GetAttribute("aria-labelledby"));
        });
        Assert.Equal(tabs.Count, tabs.Select(tab => tab.Id).Distinct().Count());
        Assert.Contains("data-[state=active]:bg-background", tabs[0].ClassList);
    }

    [Fact]
    public void The_default_tab_starts_active_and_the_other_panels_hidden()
    {
        using var context = new AdminTestContext();

        var cut = RenderTabs(context);
        var tabs = cut.FindAll("[role=tab]");
        var panels = cut.FindAll("[role=tabpanel]");

        Assert.Equal(["true", "false", "false"], tabs.Select(tab => tab.GetAttribute("aria-selected")));
        Assert.Equal(["active", "inactive", "inactive"], tabs.Select(tab => tab.GetAttribute("data-state")));
        Assert.Equal([false, true, true], panels.Select(panel => panel.HasAttribute("hidden")));
        Assert.Equal(["active", "inactive", "inactive"], panels.Select(panel => panel.GetAttribute("data-state")));
        Assert.True(tabs[2].HasAttribute("disabled"));
    }

    [Fact]
    public void Only_the_active_tab_is_in_the_tab_order_and_the_browser_handles_the_arrow_keys()
    {
        using var context = new AdminTestContext();

        var cut = RenderTabs(context);
        var tabs = cut.FindAll("[role=tab]");

        // admin.js moves between tabs with the arrow keys and switches panels on the client, through these hooks.
        Assert.False(tabs[0].HasAttribute("tabindex"));
        Assert.Equal("-1", tabs[1].GetAttribute("tabindex"));
        Assert.All(tabs, tab => Assert.Equal("tab", tab.GetAttribute("data-ui")));
        Assert.Equal("tabs", cut.Find("[data-slot=tabs]").GetAttribute("data-ui"));
        Assert.All(cut.FindAll("[role=tabpanel]"), panel => Assert.Equal("0", panel.GetAttribute("tabindex")));
        Assert.DoesNotContain(cut.FindAll("*").SelectMany(element => element.Attributes), attribute => attribute.Name.StartsWith("on", StringComparison.Ordinal));
        Assert.DoesNotContain(cut.FindAll("[class]").SelectMany(element => element.ClassList), Utilities.IsPhysical);
    }

    [Fact]
    public void Ids_do_not_depend_on_the_words_in_a_value()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<Tabs>(tabs => tabs
            .Add(t => t.DefaultValue, "two words")
            .AddChildContent<TabsList>(list => list.AddChildContent<TabsTrigger>(trigger => trigger.Add(t => t.Value, "two words")))
            .AddChildContent<TabsContent>(content => content.Add(c => c.Value, "two words")));

        Assert.DoesNotContain(" ", cut.Find("[role=tab]").Id);
        Assert.Equal(cut.Find("[role=tabpanel]").Id, cut.Find("[role=tab]").GetAttribute("aria-controls"));
    }
}
