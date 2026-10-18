using Bunit;
using FluentCMS.Admin.Components.Ui;

namespace FluentCMS.Tests.Admin;

public sealed class TextareaTests
{
    [Fact]
    public void Renders_a_native_textarea_with_the_shadcn_classes()
    {
        using var context = new AdminTestContext();

        var textarea = context.Render<Textarea>(t => t.Add(x => x.Class, "min-h-32")).Find("textarea");

        Assert.Equal("textarea", textarea.GetAttribute("data-slot"));
        Assert.Contains("field-sizing-content", textarea.ClassList);
        Assert.Contains("border-input", textarea.ClassList);
        Assert.Contains("focus-visible:ring-[3px]", textarea.ClassList);
        Assert.Contains("aria-invalid:border-destructive", textarea.ClassList);
        Assert.Contains("min-h-32", textarea.ClassList);
        Assert.False(textarea.HasAttribute("id"));
        Assert.False(textarea.HasAttribute("aria-describedby"));
    }

    [Fact]
    public void Passes_form_attributes_through_to_the_element()
    {
        using var context = new AdminTestContext();

        var textarea = context.Render<Textarea>(t => t
            .AddUnmatched("name", "Form.Notes")
            .AddUnmatched("rows", "4")
            .AddUnmatched("required", true)
            .AddUnmatched("id", "notes")).Find("textarea");

        Assert.Equal("Form.Notes", textarea.GetAttribute("name"));
        Assert.Equal("4", textarea.GetAttribute("rows"));
        Assert.True(textarea.HasAttribute("required"));
        Assert.Equal("notes", textarea.Id);
    }

    [Fact]
    public async Task On_a_static_page_its_value_is_written_as_encoded_text()
    {
        var html = await StaticHtml.RenderAsync<Textarea>(new Dictionary<string, object?>
        {
            ["name"] = "Form.Notes",
            ["value"] = "First line <b>not bold</b>",
        });

        Assert.Contains(">First line &lt;b&gt;not bold&lt;/b&gt;</textarea>", html);
        Assert.DoesNotContain("value=", html);
    }

    [Fact]
    public void Inside_a_form_field_it_takes_the_fields_id_description_and_error()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<FormField>(field => field
            .Add(f => f.Label, "Notes")
            .Add(f => f.Description, "Shown to reviewers.")
            .Add(f => f.Error, "Notes are required.")
            .AddChildContent<Textarea>());

        var textarea = cut.Find("textarea");

        Assert.Equal(textarea.Id, cut.Find("label").GetAttribute("for"));
        Assert.Equal($"{cut.Find("[data-slot=form-description]").Id} {cut.Find("[data-slot=form-message]").Id}", textarea.GetAttribute("aria-describedby"));
        Assert.Equal("true", textarea.GetAttribute("aria-invalid"));
    }
}
