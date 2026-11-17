using System.Text.Json.Nodes;
using Bunit;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Content;
using FluentCMS.Admin.Fields;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Content;

public sealed class FieldEditorTests : AdminTestContext
{
    public FieldEditorTests()
    {
        UseNeutralResources();
        Start();
    }

    [Theory]
    [InlineData("title", "title")]
    [InlineData("seo.metaTitle", "seo")]
    [InlineData("sections[0].text", "sections")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void AnErrorPathIsInTheFieldOfItsFirstSegment(string? path, string? field) =>
        ContentFormLayout.FieldOf(path).ShouldBe(field);

    [Fact]
    public void TheLabelIsTheDisplayLabelOrTheNameMadeReadable()
    {
        FieldEditorBase.LabelOf(Field("metaTitle")).ShouldBe("Meta title");
        FieldEditorBase.LabelOf(Field("seoURL")).ShouldBe("Seo url");
        FieldEditorBase.LabelOf(Field("title") with { Display = new FieldDisplayDescription { Label = "Headline" } }).ShouldBe("Headline");
    }

    [Fact]
    public void TheRegistryFindsTheEditorLastAddedForAFieldType()
    {
        var registry = new FieldEditorRegistry();
        registry.Add<JsonFieldView>("text");
        registry.Add<TextFieldEditor>("text");

        registry.Find("text")!.Editor.ShouldBe(typeof(TextFieldEditor));
        registry.Find("media").ShouldBeNull();
        Services.GetRequiredService<FieldEditorRegistry>().Find("text")!.Editor.ShouldBe(typeof(TextFieldEditor));
    }

    [Fact]
    public void TheTextEditorTakesTheLocaleAndDirectionOfTheContentAndRaisesChanges()
    {
        JsonNode? changed = null;
        var editor = Render<TextFieldEditor>(parameters => parameters
            .Add(p => p.Field, Field("title"))
            .Add(p => p.Value, JsonValue.Create("مرحبا"))
            .Add(p => p.ContentLocale, "ar")
            .Add(p => p.TextDirection, FieldEditorBase.RightToLeft)
            .Add(p => p.ValueChanged, value => changed = value));

        var input = editor.Find("input");
        (input.GetAttribute("lang"), input.GetAttribute("dir")).ShouldBe(("ar", "rtl"));
        editor.Find("label").Closest("[lang]").ShouldBeNull();
        input.Input("سلام");

        changed!.GetValue<string>().ShouldBe("سلام");
    }

    [Fact]
    public void TheFormSaysWhetherAValueIsRequired()
    {
        var editor = Render<TextFieldEditor>(parameters => parameters.Add(p => p.Field, Field("title")).Add(p => p.Required, true));

        editor.Find("input").GetAttribute("aria-required").ShouldBe("true");
    }

    [Fact]
    public void AReadOnlyTextEditorCannotBeChanged()
    {
        var editor = Render<TextFieldEditor>(parameters => parameters
            .Add(p => p.Field, Field("title"))
            .Add(p => p.ReadOnly, true));

        editor.Find("input").HasAttribute("disabled").ShouldBeTrue();
    }

    private static SchemaFieldDescription Field(string name) => new() { Name = name, Type = "text", Validation = new FieldValidationDescription() };
}
