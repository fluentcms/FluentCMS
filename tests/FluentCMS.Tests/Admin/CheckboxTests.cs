using Bunit;
using FluentCMS.Admin.Components.Ui;

namespace FluentCMS.Tests.Admin;

public sealed class CheckboxTests
{
    [Fact]
    public void Renders_a_native_checkbox_with_the_shadcn_classes_and_a_tick_for_the_checked_state()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<Checkbox>(c => c.Add(x => x.Class, "mt-1"));
        var input = cut.Find("input");
        var tick = cut.Find("svg");

        Assert.Equal("checkbox", input.GetAttribute("type"));
        Assert.Contains("appearance-none", input.ClassList);
        Assert.Contains("checked:bg-primary", input.ClassList);
        Assert.Contains("focus-visible:ring-[3px]", input.ClassList);
        Assert.Contains("aria-invalid:border-destructive", input.ClassList);
        Assert.Contains("mt-1", cut.Find("[data-slot=checkbox]").ClassList);
        Assert.Contains("peer-checked:block", tick.ClassList);
        Assert.Equal("true", tick.GetAttribute("aria-hidden"));
        Assert.False(input.HasAttribute("id"));
        Assert.False(input.HasAttribute("checked"));
    }

    [Fact]
    public void Passes_form_attributes_through_to_the_input()
    {
        using var context = new AdminTestContext();

        var input = context.Render<Checkbox>(c => c
            .AddUnmatched("name", "Form.Read")
            .AddUnmatched("value", "true")
            .AddUnmatched("checked", true)
            .AddUnmatched("disabled", true)).Find("input");

        Assert.Equal("Form.Read", input.GetAttribute("name"));
        Assert.Equal("true", input.GetAttribute("value"));
        Assert.True(input.HasAttribute("checked"));
        Assert.True(input.HasAttribute("disabled"));
    }

    [Fact]
    public void Inside_a_form_field_it_takes_the_fields_id_description_and_error()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<FormField>(field => field
            .Add(f => f.Label, "Read")
            .Add(f => f.Description, "Lets the agent read items.")
            .Add(f => f.Error, "Choose at least one operation.")
            .AddChildContent<Checkbox>());

        var input = cut.Find("input");

        Assert.Equal(input.Id, cut.Find("label").GetAttribute("for"));
        Assert.Equal($"{cut.Find("[data-slot=form-description]").Id} {cut.Find("[data-slot=form-message]").Id}", input.GetAttribute("aria-describedby"));
        Assert.Equal("true", input.GetAttribute("aria-invalid"));
    }
}
