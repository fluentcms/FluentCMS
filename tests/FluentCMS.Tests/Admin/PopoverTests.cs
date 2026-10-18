using Bunit;
using FluentCMS.Admin.Components.Ui;

namespace FluentCMS.Tests.Admin;

public sealed class PopoverTests
{
    [Fact]
    public void Trigger_toggles_a_popover_dialog_that_it_names()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<Popover>(popover => popover
            .AddChildContent<PopoverTrigger>(trigger => trigger.AddChildContent("Open"))
            .AddChildContent<PopoverContent>(content => content.Add(c => c.Class, "w-80").AddChildContent("Dimensions")));

        var trigger = cut.Find("button");
        var content = cut.Find("[data-slot=popover-content]");

        Assert.Equal(content.Id, trigger.GetAttribute("popovertarget"));
        Assert.Equal(content.Id, trigger.GetAttribute("aria-controls"));
        Assert.Equal("dialog", trigger.GetAttribute("aria-haspopup"));
        Assert.Equal("false", trigger.GetAttribute("aria-expanded"));
        Assert.Equal("popover-trigger", trigger.GetAttribute("data-ui"));
        Assert.Equal("auto", content.GetAttribute("popover"));
        Assert.Equal("dialog", content.GetAttribute("role"));
        Assert.Equal("popover", content.GetAttribute("data-ui"));
        Assert.Equal(trigger.Id, content.GetAttribute("aria-labelledby"));
        Assert.Equal("-1", content.GetAttribute("tabindex"));
        Assert.Contains("bg-popover", content.ClassList);
        Assert.Contains("w-80", content.ClassList);
    }

    [Fact]
    public void Two_popovers_never_share_an_id()
    {
        using var context = new AdminTestContext();

        var first = context.Render<Popover>(popover => popover.AddChildContent<PopoverContent>());
        var second = context.Render<Popover>(popover => popover.AddChildContent<PopoverContent>());

        Assert.NotEqual(first.Find("[popover]").Id, second.Find("[popover]").Id);
    }
}
