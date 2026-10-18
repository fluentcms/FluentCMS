using Bunit;
using FluentCMS.Admin.Components.Icons;
using FluentCMS.Admin.Components.Pages;
using FluentCMS.Admin.Components.Ui;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Tests.Admin;

public sealed class ShowcaseTests
{
    [Fact]
    public void Renders_every_component_in_the_ui_folder()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<Showcase>();

        var components = typeof(Button).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(Button).Namespace && typeof(IComponent).IsAssignableFrom(type) && !type.IsAbstract)
            .Select(type => type.Name)
            .ToHashSet();
        var rendered = cut.FindComponents<IComponent>().Select(component => component.Instance.GetType().Name).ToHashSet();

        Assert.NotEmpty(components);
        Assert.Subset(rendered, components);
    }

    [Fact]
    public void Renders_every_generated_icon()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<Showcase>();

        var icons = typeof(XIcon).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(XIcon).Namespace && typeof(IComponent).IsAssignableFrom(type))
            .Select(type => type.Name)
            .ToHashSet();
        var rendered = cut.Find("[data-testid=icons]").QuerySelectorAll("code").Select(name => name.TextContent).ToHashSet();

        Assert.NotEmpty(icons);
        Assert.Equal(icons, rendered);
        Assert.Equal(icons.Count, cut.FindAll("[data-testid=icons] svg[aria-hidden=true]").Count);
    }

    [Fact]
    public void Every_section_lists_its_keyboard_hints()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<Showcase>();

        var sections = cut.FindAll("section");

        Assert.Equal(23, sections.Count);
        Assert.All(sections, section =>
        {
            var hints = section.QuerySelector("[data-testid=keyboard-hints]");
            Assert.NotNull(hints);
            Assert.NotEmpty(hints.QuerySelectorAll("dt kbd[data-slot=kbd]"));
            Assert.NotEmpty(hints.QuerySelectorAll("dd"));
        });
    }

    [Fact]
    public async Task An_action_reports_on_the_server_status_line()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<Showcase>();
        Assert.Equal("none", cut.Find("[data-testid=last-action]").TextContent);

        await cut.Find("[data-slot=button][data-variant=secondary]").ClickAsync(new());

        Assert.StartsWith("Secondary at ", cut.Find("[data-testid=last-action]").TextContent);
    }

    [Fact]
    public async Task A_change_in_a_select_or_a_switch_reports_on_the_server_status_line()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<Showcase>();

        await cut.Find("[data-slot=select]").ChangeAsync(new ChangeEventArgs { Value = "done" });
        Assert.StartsWith("Status done at ", cut.Find("[data-testid=last-action]").TextContent);

        await cut.FindAll("input[role=switch]")[1].ChangeAsync(new ChangeEventArgs { Value = true });
        Assert.StartsWith("Notifications on at ", cut.Find("[data-testid=last-action]").TextContent);
    }

    [Fact]
    public async Task The_toast_buttons_show_toasts_on_the_page()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<Showcase>();
        Assert.Empty(cut.FindAll("[data-slot=toast]"));

        await cut.Find("[data-testid=toast-show]").ClickAsync(new());
        await cut.Find("[data-testid=toast-error]").ClickAsync(new());

        cut.WaitForAssertion(() => Assert.Equal(["default", "destructive"], cut.FindAll("[data-slot=toast]").Select(toast => toast.GetAttribute("data-variant"))));
        Assert.StartsWith("Error toast shown at ", cut.Find("[data-testid=last-action]").TextContent);
    }

    [Fact]
    public async Task The_diff_section_switches_between_the_sample_and_a_set_of_200_items()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<Showcase>();
        Assert.Equal(["update", "create", "delete"], cut.FindAll("[data-slot=diff-view-item]").Select(item => item.GetAttribute("data-operation")));

        await cut.Find("[data-testid=diff-size]").ClickAsync(new());
        cut.WaitForAssertion(() => Assert.Equal(200, cut.FindAll("[data-slot=diff-view-item]").Count));
        Assert.StartsWith("Diff of 200 items at ", cut.Find("[data-testid=last-action]").TextContent);

        await cut.Find("[data-testid=diff-size]").ClickAsync(new());
        Assert.Equal(3, cut.FindAll("[data-slot=diff-view-item]").Count);
    }

    [Fact]
    public void The_direction_link_switches_to_right_to_left_and_back()
    {
        using var context = new AdminTestContext();
        var navigation = context.Services.GetRequiredService<NavigationManager>();

        var cut = context.Render<Showcase>();
        Assert.EndsWith("?dir=rtl", cut.Find("[data-testid=direction]").GetAttribute("href"));

        navigation.NavigateTo("ui?dir=rtl");
        cut = context.Render<Showcase>();

        Assert.DoesNotContain("dir=", cut.Find("[data-testid=direction]").GetAttribute("href"));
    }
}
