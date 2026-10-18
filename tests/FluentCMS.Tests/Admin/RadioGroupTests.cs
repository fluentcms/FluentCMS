using Bunit;
using FluentCMS.Admin.Components.Ui;

namespace FluentCMS.Tests.Admin;

public sealed class RadioGroupTests
{
    [Fact]
    public void Renders_a_radiogroup_of_native_radio_buttons_with_the_shadcn_classes()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<RadioGroup>(group => group
            .Add(g => g.Class, "grid-flow-col")
            .AddChildContent<RadioGroupItem>(item => item.Add(i => i.Value, "default"))
            .AddChildContent<RadioGroupItem>(item => item.Add(i => i.Value, "compact").Add(i => i.Class, "mt-1")));

        var group = cut.Find("[data-slot=radio-group]");
        var inputs = cut.FindAll("input");
        var items = cut.FindAll("[data-slot=radio-group-item]");

        Assert.Equal("radiogroup", group.GetAttribute("role"));
        Assert.Contains("grid", group.ClassList);
        Assert.Contains("grid-flow-col", group.ClassList);
        Assert.All(inputs, input => Assert.Equal("radio", input.GetAttribute("type")));
        Assert.Equal(["default", "compact"], inputs.Select(input => input.GetAttribute("value")));
        Assert.All(inputs, input => Assert.Contains("rounded-full", input.ClassList));
        Assert.All(inputs, input => Assert.Contains("focus-visible:ring-[3px]", input.ClassList));
        Assert.Contains("mt-1", items[1].ClassList);
        Assert.All(cut.FindAll("[data-slot=radio-group-indicator]"), dot =>
        {
            Assert.Equal("true", dot.GetAttribute("aria-hidden"));
            Assert.Contains("peer-checked:block", dot.ClassList);
        });
        Assert.DoesNotContain(cut.FindAll("[class]").SelectMany(element => element.ClassList), Utilities.IsPhysical);
    }

    [Fact]
    public void Its_buttons_share_one_name_so_the_arrow_keys_move_between_them_natively()
    {
        using var context = new AdminTestContext();

        var first = context.Render<RadioGroup>(group => group
            .AddChildContent<RadioGroupItem>(item => item.Add(i => i.Value, "a"))
            .AddChildContent<RadioGroupItem>(item => item.Add(i => i.Value, "b")));
        var second = context.Render<RadioGroup>(group => group
            .AddChildContent<RadioGroupItem>(item => item.Add(i => i.Value, "a")));
        var named = context.Render<RadioGroup>(group => group
            .Add(g => g.Name, "Form.Format")
            .AddChildContent<RadioGroupItem>(item => item.Add(i => i.Value, "json")));

        var names = first.FindAll("input").Select(input => input.GetAttribute("name")).Distinct().ToList();

        Assert.Single(names);
        Assert.False(string.IsNullOrEmpty(names[0]));
        Assert.NotEqual(names[0], second.Find("input").GetAttribute("name"));
        Assert.Equal("Form.Format", named.Find("input").GetAttribute("name"));
        Assert.All(first.FindAll("input"), input => Assert.False(input.HasAttribute("tabindex")));
    }

    [Fact]
    public void The_value_checks_the_matching_button_and_an_attribute_can_disable_one()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<RadioGroup>(group => group
            .Add(g => g.Value, "comfortable")
            .AddChildContent<RadioGroupItem>(item => item.Add(i => i.Value, "default"))
            .AddChildContent<RadioGroupItem>(item => item.Add(i => i.Value, "comfortable"))
            .AddChildContent<RadioGroupItem>(item => item.Add(i => i.Value, "compact").AddUnmatched("disabled", true)));

        var inputs = cut.FindAll("input");

        Assert.Equal([false, true, false], inputs.Select(input => input.HasAttribute("checked")));
        Assert.True(inputs[2].HasAttribute("disabled"));
    }

    [Fact]
    public void Inside_a_form_field_the_label_names_the_group_and_an_error_marks_every_button()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<FormField>(field => field
            .Add(f => f.Label, "Format")
            .Add(f => f.Description, "How the export is written.")
            .AddChildContent<RadioGroup>(group => group
                .AddChildContent<RadioGroupItem>(item => item.Add(i => i.Value, "json"))
                .AddChildContent<RadioGroupItem>(item => item.Add(i => i.Value, "csv"))));

        cut.Render(field => field.Add(f => f.Error, "Choose a format."));

        var group = cut.Find("[role=radiogroup]");

        Assert.Equal(cut.Find("label").Id, group.GetAttribute("aria-labelledby"));
        Assert.Equal($"{cut.Find("[data-slot=form-description]").Id} {cut.Find("[data-slot=form-message]").Id}", group.GetAttribute("aria-describedby"));
        Assert.Equal("true", group.GetAttribute("aria-invalid"));
        Assert.All(cut.FindAll("input"), input => Assert.Equal("true", input.GetAttribute("aria-invalid")));
        Assert.All(cut.FindAll("input"), input => Assert.False(input.HasAttribute("id")));
    }
}
