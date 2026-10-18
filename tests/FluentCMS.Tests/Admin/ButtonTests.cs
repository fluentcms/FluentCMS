using Bunit;
using FluentCMS.Admin.Components.Ui;

namespace FluentCMS.Tests.Admin;

public sealed class ButtonTests
{
    [Fact]
    public void Renders_a_native_button_with_the_default_variant_and_size()
    {
        using var context = new AdminTestContext();

        var button = context.Render<Button>(b => b.AddChildContent("Save")).Find("button");

        Assert.Equal("button", button.GetAttribute("type"));
        Assert.Equal("button", button.GetAttribute("data-slot"));
        Assert.Equal("default", button.GetAttribute("data-variant"));
        Assert.Equal("default", button.GetAttribute("data-size"));
        Assert.Equal("Save", button.TextContent.Trim());
        Assert.Contains("bg-primary", button.ClassList);
        Assert.Contains("h-9", button.ClassList);
        Assert.Contains("focus-visible:ring-[3px]", button.ClassList);
        Assert.False(button.HasAttribute("disabled"));
        Assert.False(button.HasAttribute("tabindex"));
    }

    [Theory]
    [InlineData("destructive", "bg-destructive")]
    [InlineData("outline", "bg-background")]
    [InlineData("secondary", "bg-secondary")]
    [InlineData("ghost", "hover:bg-accent")]
    [InlineData("link", "underline-offset-4")]
    public void Each_variant_carries_the_shadcn_classes(string variant, string expectedClass)
    {
        using var context = new AdminTestContext();

        var button = context.Render<Button>(b => b.Add(x => x.Variant, variant)).Find("button");

        Assert.Equal(variant, button.GetAttribute("data-variant"));
        Assert.Contains(expectedClass, button.ClassList);
        Assert.DoesNotContain("bg-primary", button.ClassList);
    }

    [Theory]
    [InlineData("xs", "h-6")]
    [InlineData("sm", "h-8")]
    [InlineData("lg", "h-10")]
    [InlineData("icon", "size-9")]
    public void Each_size_replaces_the_default_height(string size, string expectedClass)
    {
        using var context = new AdminTestContext();

        var button = context.Render<Button>(b => b.Add(x => x.Size, size)).Find("button");

        Assert.Contains(expectedClass, button.ClassList);
        Assert.DoesNotContain("h-9", button.ClassList);
    }

    [Fact]
    public void The_class_parameter_overrides_the_classes_it_conflicts_with()
    {
        using var context = new AdminTestContext();

        var button = context.Render<Button>(b => b.Add(x => x.Size, "sm").Add(x => x.Class, "h-7 w-full")).Find("button");

        Assert.Contains("h-7", button.ClassList);
        Assert.DoesNotContain("h-8", button.ClassList);
        Assert.Contains("w-full", button.ClassList);
        Assert.Contains("px-3", button.ClassList);
    }

    [Fact]
    public async Task A_click_reaches_the_server_and_a_disabled_button_says_so()
    {
        using var context = new AdminTestContext();
        var clicks = 0;

        var cut = context.Render<Button>(b => b.Add(x => x.OnClick, () => clicks++).AddChildContent("Save"));
        await cut.Find("button").ClickAsync(new());
        cut.Render(b => b.Add(x => x.Disabled, true));

        Assert.Equal(1, clicks);
        Assert.True(cut.Find("button").HasAttribute("disabled"));
        Assert.Contains("disabled:opacity-50", cut.Find("button").ClassList);
    }

    [Fact]
    public void Submits_a_form_and_passes_extra_attributes_through()
    {
        using var context = new AdminTestContext();

        var button = context.Render<Button>(b => b
            .Add(x => x.Type, "submit")
            .AddUnmatched("name", "_handler")
            .AddUnmatched("value", "revoke")
            .AddUnmatched("aria-keyshortcuts", "Control+S")).Find("button");

        Assert.Equal("submit", button.GetAttribute("type"));
        Assert.Equal("_handler", button.GetAttribute("name"));
        Assert.Equal("revoke", button.GetAttribute("value"));
        Assert.Equal("Control+S", button.GetAttribute("aria-keyshortcuts"));
    }
}
