using Bunit;
using FluentCMS.Admin.Components.Ui;

namespace FluentCMS.Tests.Admin;

public sealed class SwitchTests
{
    [Fact]
    public void Renders_a_native_checkbox_with_the_switch_role_and_the_shadcn_classes()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<Switch>(s => s.Add(x => x.Class, "mt-1"));
        var input = cut.Find("input");
        var thumb = cut.Find("[data-slot=switch-thumb]");

        // A native checkbox toggles with Space and posts with its form, so the switch needs no script.
        Assert.Equal("checkbox", input.GetAttribute("type"));
        Assert.Equal("switch", input.GetAttribute("role"));
        Assert.False(input.HasAttribute("tabindex"));
        Assert.Contains("appearance-none", input.ClassList);
        Assert.Contains("rounded-full", input.ClassList);
        Assert.Contains("checked:bg-primary", input.ClassList);
        Assert.Contains("focus-visible:ring-[3px]", input.ClassList);
        Assert.Contains("mt-1", cut.Find("[data-slot=switch]").ClassList);
        Assert.Equal("true", thumb.GetAttribute("aria-hidden"));
        Assert.Contains("peer-checked:translate-x-[calc(100%-2px)]", thumb.ClassList);
        Assert.Contains("peer-disabled:opacity-50", thumb.ClassList);
        Assert.False(input.HasAttribute("id"));
        Assert.False(input.HasAttribute("checked"));
    }

    [Fact]
    public void The_thumb_starts_at_the_inline_start_and_slides_the_other_way_in_right_to_left()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<Switch>();
        var thumb = cut.Find("[data-slot=switch-thumb]");

        Assert.Contains("start-px", thumb.ClassList);
        Assert.Contains("rtl:peer-checked:-translate-x-[calc(100%-2px)]", thumb.ClassList);
        Assert.DoesNotContain(cut.FindAll("[class]").SelectMany(element => element.ClassList), Utilities.IsPhysical);
    }

    [Fact]
    public void Passes_form_attributes_through_to_the_input()
    {
        using var context = new AdminTestContext();

        var input = context.Render<Switch>(s => s
            .AddUnmatched("name", "Form.Enabled")
            .AddUnmatched("value", "true")
            .AddUnmatched("checked", true)
            .AddUnmatched("disabled", true)).Find("input");

        Assert.Equal("Form.Enabled", input.GetAttribute("name"));
        Assert.Equal("true", input.GetAttribute("value"));
        Assert.True(input.HasAttribute("checked"));
        Assert.True(input.HasAttribute("disabled"));
    }

    [Fact]
    public void Inside_a_form_field_it_takes_the_fields_id_description_and_error()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<FormField>(field => field
            .Add(f => f.Label, "Enabled")
            .Add(f => f.Description, "Disabled users cannot sign in.")
            .Add(f => f.Error, "The last administrator stays enabled.")
            .AddChildContent<Switch>());

        var input = cut.Find("input");

        Assert.Equal(input.Id, cut.Find("label").GetAttribute("for"));
        Assert.Equal($"{cut.Find("[data-slot=form-description]").Id} {cut.Find("[data-slot=form-message]").Id}", input.GetAttribute("aria-describedby"));
        Assert.Equal("true", input.GetAttribute("aria-invalid"));
    }
}
