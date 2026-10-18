using Bunit;
using FluentCMS.Admin.Components.Ui;

namespace FluentCMS.Tests.Admin;

public sealed class TooltipTests
{
    [Fact]
    public void The_trigger_is_described_by_a_manual_popover_with_the_tooltip_role()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<Tooltip>(tooltip => tooltip
            .AddChildContent<TooltipTrigger>(trigger => trigger.AddChildContent("Hover"))
            .AddChildContent<TooltipContent>(content => content.AddChildContent("Add to library")));

        var trigger = cut.Find("button");
        var content = cut.Find("[data-slot=tooltip-content]");

        Assert.Equal(content.Id, trigger.GetAttribute("aria-describedby"));
        Assert.Equal(content.Id, trigger.GetAttribute("data-tooltip"));
        Assert.Equal("tooltip-trigger", trigger.GetAttribute("data-ui"));
        Assert.Equal("manual", content.GetAttribute("popover"));
        Assert.Equal("tooltip", content.GetAttribute("role"));
        Assert.Equal("tooltip", content.GetAttribute("data-ui"));
        Assert.Equal("Add to library", content.TextContent.Trim());
        Assert.Contains("bg-foreground", content.ClassList);
        Assert.Contains("text-background", content.ClassList);
        Assert.False(content.HasAttribute("tabindex"));
    }

    [Fact]
    public async Task A_click_on_the_trigger_reaches_the_server()
    {
        using var context = new AdminTestContext();
        var clicked = false;

        var cut = context.Render<Tooltip>(tooltip => tooltip
            .AddChildContent<TooltipTrigger>(trigger => trigger.Add(t => t.OnClick, () => clicked = true).AddChildContent("Save"))
            .AddChildContent<TooltipContent>(content => content.AddChildContent("Save the item")));

        await cut.Find("button").ClickAsync(new());

        Assert.True(clicked);
    }
}
