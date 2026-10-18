using Bunit;
using FluentCMS.Admin.Components.Ui;

namespace FluentCMS.Tests.Admin;

public sealed class KbdTests
{
    [Fact]
    public void Renders_a_kbd_element_with_the_shadcn_classes_and_its_key()
    {
        using var context = new AdminTestContext();

        var kbd = context.Render<Kbd>(k => k
            .Add(x => x.Class, "ms-auto")
            .AddUnmatched("aria-label", "Command K")
            .AddChildContent("⌘K")).Find("kbd");

        Assert.Equal("kbd", kbd.GetAttribute("data-slot"));
        Assert.Equal("⌘K", kbd.TextContent);
        Assert.Equal("Command K", kbd.GetAttribute("aria-label"));
        Assert.Contains("bg-muted", kbd.ClassList);
        Assert.Contains("min-w-5", kbd.ClassList);
        Assert.Contains("pointer-events-none", kbd.ClassList);
        Assert.Contains("[[data-slot=tooltip-content]_&]:text-background", kbd.ClassList);
        Assert.Contains("ms-auto", kbd.ClassList);
        Assert.False(kbd.HasAttribute("tabindex"));
    }
}
