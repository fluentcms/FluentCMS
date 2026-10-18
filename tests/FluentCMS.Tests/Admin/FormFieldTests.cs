using Bunit;
using FluentCMS.Admin.Components.Ui;

namespace FluentCMS.Tests.Admin;

public sealed class FormFieldTests
{
    [Fact]
    public void Label_names_the_input_and_the_description_describes_it()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<FormField>(field => field
            .Add(f => f.Label, "Name")
            .Add(f => f.Description, "Shown in the audit log.")
            .AddChildContent<Input>(input => input.AddUnmatched("name", "Form.DisplayName")));

        var label = cut.Find("label");
        var input = cut.Find("input");

        Assert.Equal("Name", label.TextContent.Trim());
        Assert.Equal(input.Id, label.GetAttribute("for"));
        Assert.Equal(cut.Find("[data-slot=form-description]").Id, input.GetAttribute("aria-describedby"));
        Assert.Equal("false", label.GetAttribute("data-error"));
        Assert.False(input.HasAttribute("aria-invalid"));
        Assert.Empty(cut.FindAll("[data-slot=form-message]"));
    }

    [Fact]
    public void An_error_that_appears_later_marks_the_input_invalid_and_describes_it()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<FormField>(field => field
            .Add(f => f.Label, "Password")
            .Add(f => f.Description, "At least twelve characters.")
            .AddChildContent<Input>(input => input.Add(i => i.Type, "password")));

        cut.Render(field => field.Add(f => f.Error, "The passwords do not match."));

        var input = cut.Find("input");
        var message = cut.Find("[data-slot=form-message]");

        Assert.Equal("The passwords do not match.", message.TextContent);
        Assert.Equal("true", input.GetAttribute("aria-invalid"));
        Assert.Equal($"{cut.Find("[data-slot=form-description]").Id} {message.Id}", input.GetAttribute("aria-describedby"));
        Assert.Equal("true", cut.Find("label").GetAttribute("data-error"));
    }

    [Fact]
    public void Two_fields_never_share_an_id()
    {
        using var context = new AdminTestContext();

        var first = context.Render<FormField>(field => field.Add(f => f.Label, "Email").AddChildContent<Input>());
        var second = context.Render<FormField>(field => field.Add(f => f.Label, "Password").AddChildContent<Input>());

        Assert.NotEqual(first.Find("input").Id, second.Find("input").Id);
    }
}
