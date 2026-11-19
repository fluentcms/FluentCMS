using System.Globalization;
using System.Text.Json.Nodes;
using Bunit;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Components;
using FluentCMS.Admin.Fields;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.RichText.Tests;

public sealed class RichTextFieldEditorTests : BunitContext
{
    private const string Document = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Hello"}]}]}""";

    private readonly FakeEditorRuntime runtime = new();
    private readonly List<JsonNode?> changes = [];

    public RichTextFieldEditorTests()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
        Services.AddCmsComponents();
        Services.AddLocalization();
        Services.AddSingleton<IJSRuntime>(runtime);
    }

    private FakeEditor Editor => runtime.Editor;

    [Fact]
    public void TheToolbarIsLabelledAndEachButtonHasItsNameAndShortcut()
    {
        var cut = Edit(JsonNode.Parse(Document));

        var toolbar = cut.Find("[role=toolbar]");
        toolbar.GetAttribute("aria-label").ShouldBe("Formatting of Body");
        var buttons = toolbar.QuerySelectorAll("button");
        buttons.Length.ShouldBe(RichTextFieldEditor.Buttons.Length - 1);
        buttons.Select(button => button.GetAttribute("aria-label")).ShouldBe(
            ["Bold", "Italic", "Underline", "Strikethrough", "Code", "Paragraph", "Heading 1", "Heading 2", "Heading 3", "Bulleted list",
                "Numbered list", "Quote", "Code block", "Horizontal line", "Link", "Link to an entry", "Embed media", "Undo", "Redo"]);
        buttons.ShouldAllBe(button => !string.IsNullOrEmpty(button.GetAttribute("aria-keyshortcuts")));
        buttons[0].GetAttribute("title").ShouldBe("Bold (Ctrl+B)");
        buttons[0].GetAttribute("aria-pressed").ShouldBe("false");
        buttons[^1].HasAttribute("aria-pressed").ShouldBeFalse();
    }

    [Fact]
    public void EveryTextOfTheEditorIsInItsResources()
    {
        var localizer = Services.GetRequiredService<IStringLocalizer<RichTextResources>>();

        foreach (var name in RichTextFieldEditor.Buttons.Select(button => button.Resource).Concat(
            ["RichText.Toolbar.Label", "RichText.Toolbar.Shortcut", "RichText.Toolbar.Hint", "RichText.Link.Address", "RichText.Link.Help", "RichText.Link.Apply",
                "RichText.Link.Remove", "RichText.Link.Cancel", "RichText.Link.Unsafe", "RichText.Kept.Media", "RichText.Kept.InlineBlock", "RichText.Editor.Unavailable",
                "RichText.EntryLink.Label", "RichText.EntryLink.Type", "RichText.EntryLink.Choose", "RichText.EntryLink.Remove", "RichText.EntryLink.NoTypes",
                "RichText.EntryLink.Current", "RichText.EntryLink.Title", "RichText.EntryLink.Missing", "RichText.EntryLink.Unreadable", "RichText.EntryLink.Unknown",
                "RichText.Media.Title", "RichText.Media.Alt", "RichText.Media.AltHelp", "RichText.Media.AltHelpOwn", "RichText.Media.Done", "RichText.Media.Replace",
                "RichText.Media.Remove", "RichText.Media.Cancel", "RichText.Media.Missing", "RichText.Media.Failed", "RichText.Media.Unavailable", "RichText.Media.Disabled",
                "RichText.Block.Type", "RichText.Block.Insert", "RichText.Block.Cancel", "RichText.Block.Done", "RichText.Block.Duplicate", "RichText.Block.Remove",
                "RichText.Block.InsertTitle", "RichText.Block.Title", "RichText.Block.Unknown", "RichText.Block.NoForm", "RichText.Block.NoTypes", "RichText.Block.Duplicated"]))
        {
            localizer[name].ResourceNotFound.ShouldBeFalse(name);
        }
    }

    [Fact]
    public void TheEditorIsCreatedWithTheValueTheLabelAndTheLocale()
    {
        var cut = Edit(JsonNode.Parse(Document), locale: "ar", direction: FieldEditorBase.RightToLeft, help: "The article's text");

        cut.WaitForAssertion(() => runtime.Module.Created.ShouldNotBeNull());
        var created = runtime.Module.Created!;
        ((string?)created[3]).ShouldBe(JsonNode.Parse(Document)!.ToJsonString());
        var options = (RichTextFieldEditor.EditorOptions)created[4]!;
        options.Editable.ShouldBeTrue();
        options.Lang.ShouldBe("ar");
        options.Dir.ShouldBe("rtl");
        cut.Find($"#{options.LabelledBy}").TextContent.Trim().ShouldBe("Body");
        cut.Find("[role=toolbar]").GetAttribute("aria-controls").ShouldBe(options.TextboxId);
        cut.Find("button[data-command=bold]").GetAttribute("aria-keyshortcuts").ShouldBe("Control+B");
        options.DescribedBy!.Split(' ').Select(id => cut.Find($"#{id}").TextContent).ShouldContain("The article's text");
        options.Labels.Media.ShouldBe("Media");
    }

    [Fact]
    public async Task AChangeInTheEditorIsTheFieldsValueAndDoesNotComeBackAsANewDocument()
    {
        var cut = Edit(JsonNode.Parse(Document));
        cut.WaitForAssertion(() => runtime.Module.Created.ShouldNotBeNull());
        const string Edited = """{"type":"doc","content":[{"type":"heading","attrs":{"level":2},"content":[{"type":"text","text":"Crème"}]}]}""";

        await cut.InvokeAsync(() => cut.Instance.OnChangeAsync(Edited));
        cut.Render(parameters => parameters.Add(editor => editor.Value, changes[^1]));

        JsonNode.DeepEquals(changes.Single(), JsonNode.Parse(Edited)).ShouldBeTrue();
        Editor.CallsOf("setContent").ShouldBeEmpty();
    }

    [Fact]
    public async Task AnEmptyDocumentClearsTheValueAndTheSameDocumentIsNoChange()
    {
        var cut = Edit(JsonNode.Parse(Document));
        cut.WaitForAssertion(() => runtime.Module.Created.ShouldNotBeNull());

        await cut.InvokeAsync(() => cut.Instance.OnChangeAsync(Document));
        changes.ShouldBeEmpty();

        await cut.InvokeAsync(() => cut.Instance.OnChangeAsync(null));
        changes.ShouldBe([null]);
    }

    [Fact]
    public void AValueTheFormSetsReplacesTheDocument()
    {
        var cut = Edit(JsonNode.Parse(Document));
        cut.WaitForAssertion(() => runtime.Module.Created.ShouldNotBeNull());

        var theirs = JsonNode.Parse("""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Theirs"}]}]}""");
        cut.Render(parameters => parameters.Add(editor => editor.Value, theirs));

        cut.WaitForAssertion(() => Editor.CallsOf("setContent").Single().ShouldBe([theirs!.ToJsonString()]));
    }

    [Fact]
    public async Task TheSameDocumentReadBackWithTheServersOrderOfAttributesIsNotSetAgain()
    {
        var cut = Edit(JsonNode.Parse(Document));
        cut.WaitForAssertion(() => runtime.Module.Created.ShouldNotBeNull());
        await cut.InvokeAsync(() => cut.Instance.OnChangeAsync("""{"type":"doc","content":[{"type":"media","attrs":{"mediaId":"5c3a1e2f-9b8d-4c7e-a6f5-0d1b2c3e4f5a","alt":"A"}}]}"""));

        cut.Render(parameters => parameters.Add(editor => editor.Value,
            JsonNode.Parse("""{"type":"doc","content":[{"type":"media","attrs":{"alt":"A","mediaId":"5c3a1e2f-9b8d-4c7e-a6f5-0d1b2c3e4f5a"}}]}""")));

        Editor.CallsOf("setContent").ShouldBeEmpty();
    }

    [Fact]
    public void ReadOnlyHasNoToolbarAndAnEditorThatCannotBeChanged()
    {
        var cut = Edit(JsonNode.Parse(Document), readOnly: true);
        cut.WaitForAssertion(() => runtime.Module.Created.ShouldNotBeNull());

        cut.FindAll("[role=toolbar]").ShouldBeEmpty();
        runtime.Module.Created![1].ShouldBeNull();
        ((RichTextFieldEditor.EditorOptions)runtime.Module.Created[4]!).Editable.ShouldBeFalse();

        cut.Render(parameters => parameters.Add(editor => editor.ReadOnly, false));
        cut.WaitForAssertion(() => Editor.CallsOf("setEditable").Single().ShouldBe([true]));
        cut.Find("[role=toolbar]").ShouldNotBeNull();
        Editor.CallsOf("setToolbar").Single()[0].ShouldBeOfType<ElementReference>();
    }

    [Fact]
    public async Task TheButtonsOfTheActiveMarksAndNodesArePressed()
    {
        var cut = Edit(JsonNode.Parse(Document));
        cut.WaitForAssertion(() => runtime.Module.Created.ShouldNotBeNull());

        await cut.InvokeAsync(() => cut.Instance.OnStateAsync(["bold", "heading2"]));

        cut.Find("button[data-command=bold]").GetAttribute("aria-pressed").ShouldBe("true");
        cut.Find("button[data-command=heading2]").GetAttribute("aria-pressed").ShouldBe("true");
        cut.Find("button[data-command=italic]").GetAttribute("aria-pressed").ShouldBe("false");
    }

    [Fact]
    public void AButtonRunsItsCommandInTheEditor()
    {
        var cut = Edit(JsonNode.Parse(Document));
        cut.WaitForAssertion(() => runtime.Module.Created.ShouldNotBeNull());

        cut.Find("button[data-command=orderedList]").Click();

        cut.WaitForAssertion(() => Editor.CallsOf("run").Single().ShouldBe(["orderedList"]));
    }

    [Fact]
    public void ALinkTheServerWouldRefuseIsNotSetAndSaysWhy()
    {
        var cut = Edit(JsonNode.Parse(Document));
        cut.WaitForAssertion(() => runtime.Module.Created.ShouldNotBeNull());

        cut.Find("button[data-command=link]").Click();
        var address = cut.WaitForElement(".rich-text-link input");
        address.Input("javascript:alert(1)");
        cut.Find(".rich-text-link input").KeyDown("Enter");

        cut.WaitForAssertion(() => cut.Find(".rich-text-link").TextContent.ShouldContain("Use an http, https, mailto or tel address, or a relative link."));
        Editor.CallsOf("setLink").Single().ShouldBe(["javascript:alert(1)"]);

        cut.Find(".rich-text-link input").Input("https://example.com/");
        cut.Find(".rich-text-link input").KeyDown("Enter");

        cut.WaitForAssertion(() => cut.FindAll(".rich-text-link").ShouldBeEmpty());
        Editor.CallsOf("setLink").Last().ShouldBe(["https://example.com/"]);
    }

    [Fact]
    public void ALinkAtTheSelectionCanBeRemovedAndEscapeReturnsToTheText()
    {
        Editor.Link = "https://example.com/old";
        var cut = Edit(JsonNode.Parse(Document));
        cut.WaitForAssertion(() => runtime.Module.Created.ShouldNotBeNull());

        cut.InvokeAsync(() => cut.Instance.OnLinkRequestedAsync());
        cut.WaitForAssertion(() => cut.Find(".rich-text-link input").GetAttribute("value").ShouldBe("https://example.com/old"));
        cut.FindAll(".rich-text-link fluent-button").Select(button => button.TextContent.Trim()).ShouldBe(["Apply", "Remove link", "Cancel"]);

        cut.Find(".rich-text-link").KeyDown("Escape");

        cut.WaitForAssertion(() => cut.FindAll(".rich-text-link").ShouldBeEmpty());
        Editor.CallsOf("focus").ShouldNotBeEmpty();
        Editor.CallsOf("setLink").ShouldBeEmpty();
    }

    [Fact]
    public void TheServersErrorIsShownAtTheFieldAndTheEditorIsInvalid()
    {
        var cut = Edit(JsonNode.Parse(Document));
        cut.WaitForAssertion(() => runtime.Module.Created.ShouldNotBeNull());

        cut.Render(parameters => parameters.Add(editor => editor.Errors, new List<ApiProblemError>
        {
            new() { Path = "body.content[0].content[0].marks[0].attrs.href", Message = "The href must be a safe URL." },
        }));

        cut.WaitForAssertion(() => Editor.CallsOf("describe").ShouldNotBeEmpty());
        var described = Editor.CallsOf("describe").Last();
        described[1].ShouldBe(true);
        ((string)described[0]!).Split(' ').Select(id => cut.Find($"#{id}").TextContent).ShouldContain("The href must be a safe URL.");
        cut.Find(".rich-text-frame").ClassList.ShouldContain("rich-text-frame-invalid");
    }

    [Fact]
    public void AModuleThatDoesNotLoadSaysSoAndShowsTheValueAsItIs()
    {
        runtime.ImportFails = new JSException("Failed to fetch the module.");

        var cut = Edit(JsonNode.Parse(Document));

        cut.WaitForAssertion(() => cut.Find("[role=alert]").TextContent.ShouldContain("could not be loaded"));
        JsonNode.DeepEquals(JsonNode.Parse(cut.Find("pre.rich-text-json").TextContent), JsonNode.Parse(Document)).ShouldBeTrue();
        cut.FindAll("[role=toolbar]").ShouldBeEmpty();
        changes.ShouldBeEmpty();
    }

    [Fact]
    public void AValueTheEditorCannotHoldWholeIsShownAsItIsAndNeverWritten()
    {
        runtime.Module.Refuses = true;
        const string Aligned = """{"type":"doc","content":[{"type":"paragraph","attrs":{"textAlign":"center"},"content":[{"type":"text","text":"Centred"}]}]}""";

        var cut = Edit(JsonNode.Parse(Aligned));

        cut.WaitForAssertion(() => cut.Find("pre.rich-text-json").TextContent.ShouldContain("textAlign"));
        cut.Find(".cms-field-message").TextContent.ShouldContain("cannot change without losing");
        cut.FindAll("[role=toolbar]").ShouldBeEmpty();
        cut.Find(".rich-text-frame").HasAttribute("hidden").ShouldBeTrue();
        changes.ShouldBeEmpty();
    }

    [Fact]
    public void AValueTheFormSetsThatTheEditorCannotHoldStopsTheEditor()
    {
        var cut = Edit(JsonNode.Parse(Document));
        cut.WaitForAssertion(() => runtime.Module.Created.ShouldNotBeNull());
        Editor.RefusesContent = true;

        cut.Render(parameters => parameters.Add(editor => editor.Value, JsonNode.Parse("""{"type":"doc","content":[{"type":"table"}]}""")));

        cut.WaitForAssertion(() => cut.Find("pre.rich-text-json").TextContent.ShouldContain("table"));
        Editor.CallsOf("dispose").ShouldHaveSingleItem();
        changes.ShouldBeEmpty();
    }

    [Fact]
    public async Task ADocumentWhoseMarksTheEditorReorderedIsNoChange()
    {
        const string Stored = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","marks":[{"type":"bold"},{"type":"link","attrs":{"href":"/"}}],"text":"a"}]}]}""";
        var cut = Edit(JsonNode.Parse(Stored));
        cut.WaitForAssertion(() => runtime.Module.Created.ShouldNotBeNull());

        await cut.InvokeAsync(() => cut.Instance.OnChangeAsync("""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","marks":[{"type":"link","attrs":{"href":"/"}},{"type":"bold"}],"text":"a"}]}]}"""));

        changes.ShouldBeEmpty();
    }

    [Fact]
    public async Task AFlushGivesTheFormTheChangeTheModuleHeldBack()
    {
        var cut = Edit(JsonNode.Parse(Document));
        cut.WaitForAssertion(() => runtime.Module.Created.ShouldNotBeNull());
        await cut.InvokeAsync(() => cut.Instance.FlushAsync().AsTask());
        changes.ShouldBeEmpty();

        Editor.HasPending = true;
        Editor.Pending = """{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":"Typed"}]}]}""";
        await cut.InvokeAsync(() => cut.Instance.FlushAsync().AsTask());

        changes.Single()!.ToJsonString().ShouldContain("Typed");
    }

    [Fact]
    public async Task AChangeHeldBackGoesToTheFormAsTheEditorGoes()
    {
        var cut = Edit(JsonNode.Parse(Document));
        cut.WaitForAssertion(() => runtime.Module.Created.ShouldNotBeNull());
        Editor.HasPending = true;
        Editor.Pending = null;

        await DisposeComponentsAsync();

        changes.ShouldBe([null]);
    }

    [Fact]
    public void OnAMacTheShortcutsNameCommand()
    {
        runtime.Module.Mac = true;

        var cut = Edit(JsonNode.Parse(Document));

        cut.WaitForAssertion(() => cut.Find("button[data-command=bold]").GetAttribute("aria-keyshortcuts").ShouldBe("Meta+B"));
        cut.Find("button[data-command=bold]").GetAttribute("title").ShouldBe("Bold (Cmd+B)");
        cut.Find("button[data-command=heading1]").GetAttribute("aria-keyshortcuts").ShouldBe("Meta+Alt+1");
    }

    [Fact]
    public async Task DisposingTheEditorDisposesItsModule()
    {
        var cut = Edit(JsonNode.Parse(Document));
        cut.WaitForAssertion(() => runtime.Module.Created.ShouldNotBeNull());

        await DisposeComponentsAsync();

        Editor.CallsOf("dispose").ShouldHaveSingleItem();
        Editor.Disposed.ShouldBeTrue();
        runtime.Module.Disposed.ShouldBeTrue();
    }

    private IRenderedComponent<RichTextFieldEditor> Edit(JsonNode? value, bool readOnly = false, string locale = "en", string direction = FieldEditorBase.LeftToRight, string? help = null) =>
        Render<RichTextFieldEditor>(parameters => parameters
            .Add(editor => editor.Field, new SchemaFieldDescription
            {
                Name = "body",
                Type = "richText",
                Validation = new FieldValidationDescription(),
                Display = help is null ? null : new FieldDisplayDescription { Help = help },
            })
            .Add(editor => editor.Value, value)
            .Add(editor => editor.ReadOnly, readOnly)
            .Add(editor => editor.ContentLocale, locale)
            .Add(editor => editor.TextDirection, direction)
            .Add(editor => editor.ValueChanged, EventCallback.Factory.Create<JsonNode?>(this, changed => changes.Add(changed))));
}
