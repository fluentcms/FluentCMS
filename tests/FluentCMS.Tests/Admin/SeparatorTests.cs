using Bunit;
using FluentCMS.Admin.Components.Ui;

namespace FluentCMS.Tests.Admin;

public sealed class SeparatorTests
{
    [Fact]
    public void Renders_a_decorative_horizontal_rule_by_default()
    {
        using var context = new AdminTestContext();

        var separator = context.Render<Separator>(s => s.Add(x => x.Class, "my-4")).Find("[data-slot=separator]");

        Assert.Equal("none", separator.GetAttribute("role"));
        Assert.Equal("horizontal", separator.GetAttribute("data-orientation"));
        Assert.False(separator.HasAttribute("aria-orientation"));
        Assert.Contains("bg-border", separator.ClassList);
        Assert.Contains("data-[orientation=horizontal]:h-px", separator.ClassList);
        Assert.Contains("data-[orientation=vertical]:w-px", separator.ClassList);
        Assert.Contains("my-4", separator.ClassList);
        Assert.Empty(separator.ChildNodes);
    }

    [Theory]
    [InlineData("horizontal", null)]
    [InlineData("vertical", "vertical")]
    public void A_separator_that_is_not_decorative_is_exposed_with_its_orientation(string orientation, string? ariaOrientation)
    {
        using var context = new AdminTestContext();

        var separator = context.Render<Separator>(s => s
            .Add(x => x.Orientation, orientation)
            .Add(x => x.Decorative, false)).Find("[data-slot=separator]");

        Assert.Equal("separator", separator.GetAttribute("role"));
        Assert.Equal(orientation, separator.GetAttribute("data-orientation"));
        Assert.Equal(ariaOrientation, separator.GetAttribute("aria-orientation"));
    }

    [Fact]
    public void Takes_no_focus()
    {
        using var context = new AdminTestContext();

        var separator = context.Render<Separator>(s => s.Add(x => x.Orientation, "vertical")).Find("[data-slot=separator]");

        Assert.False(separator.HasAttribute("tabindex"));
    }
}
