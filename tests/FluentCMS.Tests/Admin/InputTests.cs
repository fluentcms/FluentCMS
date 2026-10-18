using Bunit;
using FluentCMS.Admin.Components.Ui;

namespace FluentCMS.Tests.Admin;

public sealed class InputTests
{
    [Fact]
    public void Renders_a_native_text_input_with_the_shadcn_classes()
    {
        using var context = new AdminTestContext();

        var input = context.Render<Input>(i => i.Add(x => x.Class, "max-w-xs")).Find("input");

        Assert.Equal("text", input.GetAttribute("type"));
        Assert.Equal("input", input.GetAttribute("data-slot"));
        Assert.Contains("border-input", input.ClassList);
        Assert.Contains("focus-visible:ring-[3px]", input.ClassList);
        Assert.Contains("aria-invalid:border-destructive", input.ClassList);
        Assert.Contains("max-w-xs", input.ClassList);
        Assert.False(input.HasAttribute("id"));
        Assert.False(input.HasAttribute("aria-describedby"));
    }

    [Fact]
    public void Passes_form_attributes_through_to_the_element()
    {
        using var context = new AdminTestContext();

        var input = context.Render<Input>(i => i
            .Add(x => x.Type, "email")
            .AddUnmatched("name", "Form.Email")
            .AddUnmatched("value", "owner@example.com")
            .AddUnmatched("autocomplete", "username")
            .AddUnmatched("required", true)
            .AddUnmatched("id", "email")).Find("input");

        Assert.Equal("email", input.GetAttribute("type"));
        Assert.Equal("Form.Email", input.GetAttribute("name"));
        Assert.Equal("owner@example.com", input.GetAttribute("value"));
        Assert.Equal("username", input.GetAttribute("autocomplete"));
        Assert.True(input.HasAttribute("required"));
        Assert.Equal("email", input.Id);
    }
}
