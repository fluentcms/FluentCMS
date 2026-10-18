using Bunit;
using FluentCMS.Admin.Components.Ui;
using Microsoft.AspNetCore.Components;

namespace FluentCMS.Tests.Admin;

public sealed class SelectTests
{
    private static readonly RenderFragment Options = builder =>
    {
        builder.OpenElement(0, "option");
        builder.AddAttribute(1, "value", "todo");
        builder.AddContent(2, "Todo");
        builder.CloseElement();
        builder.OpenElement(3, "option");
        builder.AddAttribute(4, "value", "done");
        builder.AddContent(5, "Done");
        builder.CloseElement();
    };

    [Fact]
    public void Renders_a_native_select_with_the_shadcn_classes_and_a_chevron_at_the_inline_end()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<Select>(s => s.Add(x => x.Class, "min-w-40").Add(x => x.ChildContent, Options));
        var select = cut.Find("select");
        var chevron = cut.Find("svg");

        Assert.Equal("select-wrapper", select.ParentElement!.GetAttribute("data-slot"));
        Assert.Contains("has-[select:disabled]:opacity-50", select.ParentElement.ClassList);
        Assert.Contains("appearance-none", select.ClassList);
        Assert.Contains("pe-9", select.ClassList);
        Assert.Contains("focus-visible:ring-[3px]", select.ClassList);
        Assert.Contains("aria-invalid:border-destructive", select.ClassList);
        Assert.Contains("min-w-40", select.ClassList);
        Assert.Equal(["todo", "done"], select.QuerySelectorAll("option").Select(option => option.GetAttribute("value")));
        Assert.Contains("end-3.5", chevron.ClassList);
        Assert.Contains("pointer-events-none", chevron.ClassList);
        Assert.Equal("true", chevron.GetAttribute("aria-hidden"));
        Assert.False(select.HasAttribute("id"));
    }

    [Fact]
    public void Uses_logical_utilities_only_so_the_chevron_follows_the_root_direction()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<Select>(s => s.Add(x => x.ChildContent, Options));

        Assert.DoesNotContain(cut.FindAll("[class]").SelectMany(element => element.ClassList), Utilities.IsPhysical);
    }

    [Fact]
    public void Passes_form_attributes_through_and_selects_the_option_of_its_value()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<Select>(s => s
            .AddUnmatched("name", "Form.Status")
            .AddUnmatched("value", "done")
            .AddUnmatched("required", true)
            .Add(x => x.ChildContent, Options));
        var select = cut.Find("select");

        Assert.Equal("Form.Status", select.GetAttribute("name"));
        Assert.True(select.HasAttribute("required"));
        Assert.Equal("done", select.GetAttribute("value"));
    }

    [Fact]
    public async Task On_a_static_page_its_value_marks_the_matching_option_selected()
    {
        var html = await StaticHtml.RenderAsync<Select>(new Dictionary<string, object?>
        {
            ["name"] = "Form.Status",
            ["value"] = "done",
            [nameof(Select.ChildContent)] = Options,
        });

        Assert.Contains("<option value=\"todo\">Todo</option>", html);
        Assert.Contains("<option value=\"done\" selected>Done</option>", html);
    }

    [Fact]
    public async Task A_change_reaches_the_server()
    {
        using var context = new AdminTestContext();
        string? chosen = null;

        var cut = context.Render<Select>(s => s
            .AddUnmatched("onchange", EventCallback.Factory.Create<ChangeEventArgs>(this, e => chosen = (string?)e.Value))
            .Add(x => x.ChildContent, Options));

        await cut.Find("select").ChangeAsync(new ChangeEventArgs { Value = "done" });

        Assert.Equal("done", chosen);
    }

    [Fact]
    public void Inside_a_form_field_it_takes_the_fields_id_description_and_error()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<FormField>(field => field
            .Add(f => f.Label, "Status")
            .Add(f => f.Description, "Where the item stands.")
            .Add(f => f.Error, "Choose a status.")
            .AddChildContent<Select>(s => s.Add(x => x.ChildContent, Options)));

        var select = cut.Find("select");

        Assert.Equal(select.Id, cut.Find("label").GetAttribute("for"));
        Assert.Equal($"{cut.Find("[data-slot=form-description]").Id} {cut.Find("[data-slot=form-message]").Id}", select.GetAttribute("aria-describedby"));
        Assert.Equal("true", select.GetAttribute("aria-invalid"));
    }
}
