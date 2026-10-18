using Bunit;
using FluentCMS.Admin.Components.Ui;

namespace FluentCMS.Tests.Admin;

public sealed class BadgeTests
{
    [Fact]
    public void Renders_the_default_variant_with_its_content_and_extra_attributes()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<Badge>(badge => badge
            .AddUnmatched("data-testid", "initiator-kind")
            .AddChildContent("Agent"));

        var badge = cut.Find("span[data-slot=badge]");

        Assert.Equal("Agent", badge.TextContent.Trim());
        Assert.Equal("default", badge.GetAttribute("data-variant"));
        Assert.Equal("initiator-kind", badge.GetAttribute("data-testid"));
        Assert.Contains("bg-primary", badge.ClassList);
        Assert.Contains("rounded-md", badge.ClassList);
    }

    [Theory]
    [InlineData("secondary", "bg-secondary")]
    [InlineData("destructive", "bg-destructive")]
    [InlineData("outline", "text-foreground")]
    public void Each_variant_carries_the_shadcn_classes(string variant, string expectedClass)
    {
        using var context = new AdminTestContext();

        var cut = context.Render<Badge>(badge => badge
            .Add(b => b.Variant, variant)
            .Add(b => b.Class, "ms-2")
            .AddChildContent("User"));

        var badge = cut.Find("[data-slot=badge]");

        Assert.Equal(variant, badge.GetAttribute("data-variant"));
        Assert.Contains(expectedClass, badge.ClassList);
        Assert.DoesNotContain("bg-primary", badge.ClassList);
        Assert.Contains("ms-2", badge.ClassList);
    }
}
