using System.Text.Json.Nodes;
using Bunit;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Components;
using FluentCMS.Admin.Fields;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.RichText.Tests;

// The sweep of spec 7 for the rich text editor, which has no page of its own: the editor with its link panel and a refusal,
// and the editor read-only, under both pseudo-cultures, the field's label marked as data.
public sealed class LocalizationTests : BunitContext
{
    private const string Document = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Hello"}]}]}""";

    private readonly FakeEditorRuntime runtime = new();

    public LocalizationTests()
    {
        Services.AddCmsComponents();
        Services.AddLocalization();
        Services.AddSingleton<IJSRuntime>(runtime);
    }

    [Theory]
    [InlineData(PseudoCulture.LeftToRight)]
    [InlineData(PseudoCulture.RightToLeft)]
    public void EveryTextOfTheEditorAndItsLinkPanelComesFromTheResources(string culture)
    {
        this.UsePseudoCulture(culture);
        var cut = Edit(culture, readOnly: false);
        cut.WaitForAssertion(() => runtime.Module.Created.ShouldNotBeNull());

        cut.Find("button[data-command=link]").Click();
        cut.WaitForElement(".rich-text-link input").Input("javascript:alert(1)");
        cut.Find(".rich-text-link input").KeyDown("Enter");

        cut.WaitForAssertion(() => cut.FindAll(".rich-text-link [role=alert], .rich-text-link .cms-field-message").ShouldNotBeEmpty());
        cut.ShouldBeLocalized(page: false);
    }

    [Theory]
    [InlineData(PseudoCulture.LeftToRight)]
    [InlineData(PseudoCulture.RightToLeft)]
    public void EveryTextOfTheEditorReadOnlyComesFromTheResources(string culture)
    {
        this.UsePseudoCulture(culture);

        var cut = Edit(culture, readOnly: true);

        cut.WaitForAssertion(() => runtime.Module.Created.ShouldNotBeNull());
        cut.ShouldBeLocalized(page: false);
    }

    private IRenderedComponent<RichTextFieldEditor> Edit(string culture, bool readOnly) =>
        Render<RichTextFieldEditor>(parameters => parameters
            .Add(editor => editor.Field, new SchemaFieldDescription
            {
                Name = "body",
                Type = "richText",
                Validation = new FieldValidationDescription(),
                Display = new FieldDisplayDescription { Label = PseudoCulture.Transform("Body", culture), Help = PseudoCulture.Transform("The article's text", culture) },
            })
            .Add(editor => editor.Value, JsonNode.Parse(Document))
            .Add(editor => editor.ReadOnly, readOnly)
            .Add(editor => editor.ContentLocale, "en")
            .Add(editor => editor.TextDirection, FieldEditorBase.LeftToRight));
}
