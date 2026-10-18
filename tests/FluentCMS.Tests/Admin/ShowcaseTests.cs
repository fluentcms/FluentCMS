using Bunit;
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
    public void Every_section_lists_its_keyboard_hints()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<Showcase>();

        var sections = cut.FindAll("section");

        Assert.Equal(9, sections.Count);
        Assert.All(sections, section =>
        {
            var hints = section.QuerySelector("[data-testid=keyboard-hints]");
            Assert.NotNull(hints);
            Assert.NotEmpty(hints.QuerySelectorAll("dt kbd"));
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
