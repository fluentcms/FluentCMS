using System.Globalization;
using System.Text.Json.Nodes;
using AngleSharp.Dom;
using Bunit;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Components;
using FluentCMS.Admin.Content;
using FluentCMS.Admin.Fields;
using FluentCMS.Admin.Localization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace FluentCMS.Admin.Tests.Content;

// Each editor of a core field type under both pseudo-cultures: its texts, its errors, read-only, the locale of its value,
// and a value sent in the same form whatever the user's formats.
public sealed class FieldEditorTests : AdminTestContext
{
    public static readonly string[] CoreTypes = ["text", "longText", "integer", "decimal", "boolean", "date", "dateTime", "select", "slug", "json", "email", "url"];

    public static TheoryData<string> Cultures => [PseudoCulture.LeftToRight, PseudoCulture.RightToLeft];

    public static TheoryData<string, string> TypesInCultures
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var type in CoreTypes.Append("multipleSelect"))
            {
                data.Add(type, PseudoCulture.LeftToRight);
                data.Add(type, PseudoCulture.RightToLeft);
            }

            return data;
        }
    }

    private CultureInfo culture = CultureInfo.GetCultureInfo(PseudoCulture.LeftToRight);

    private FormatService Formats => Services.GetRequiredService<FormatService>();

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
        FieldEditorBase.LabelOf(Plain("metaTitle")).ShouldBe("Meta title");
        FieldEditorBase.LabelOf(Plain("seoURL")).ShouldBe("Seo url");
        FieldEditorBase.LabelOf(Plain("title") with { Display = new FieldDisplayDescription { Label = "Headline" } }).ShouldBe("Headline");
    }

    [Fact]
    public void TheRegistryFindsTheEditorLastAddedForAFieldType()
    {
        Begin(PseudoCulture.LeftToRight);
        var registry = new FieldEditorRegistry();
        registry.Add<JsonFieldView>("text");
        registry.Add<TextFieldEditor>("text");

        registry.Find("text")!.Editor.ShouldBe(typeof(TextFieldEditor));
        registry.Find("media").ShouldBeNull();
    }

    [Fact]
    public void EveryCoreFieldTypeHasAnEditorAndTheRestShowTheirJson()
    {
        Begin(PseudoCulture.LeftToRight);
        var editors = Services.GetRequiredService<FieldEditorRegistry>();

        foreach (var type in CoreTypes)
        {
            editors.Find(type).ShouldNotBeNull(type);
        }

        editors.Find("relation")!.Editor.ShouldBe(typeof(RelationFieldEditor));
        editors.Find("component")!.Editor.ShouldBe(typeof(NestedFieldEditor));
        editors.Find("blocks")!.Editor.ShouldBe(typeof(NestedFieldEditor));
        foreach (var type in new[] { "richText", "media" })
        {
            editors.Find(type).ShouldBeNull(type);
        }
    }

    [Theory]
    [MemberData(nameof(TypesInCultures))]
    public void EveryEditorHasOnlyLocalizedTextsAndShowsItsErrorsOnItsInput(string type, string pseudo)
    {
        Begin(pseudo);
        var error = Data("Too long");

        var editor = RenderEditor(FieldOf(type), parameters: new() { [nameof(FieldEditorBase.Errors)] = new List<ApiProblemError> { new() { Path = "field", Message = error } } });

        PseudoCulture.Unlocalized(editor.Nodes).ShouldBeEmpty();
        var invalid = editor.Find("[aria-invalid=true]");
        var described = invalid.GetAttribute("aria-describedby")!.Split(' ').Select(id => editor.Find($"#{id}").TextContent);
        described.ShouldContain(text => text.Contains(error, StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(TypesInCultures))]
    public void EveryEditorIsReadOnlyWhenTold(string type, string pseudo)
    {
        Begin(pseudo);
        var editor = RenderEditor(FieldOf(type), readOnly: true);

        var controls = editor.FindAll("input, textarea, select, fluent-switch, fluent-checkbox");
        controls.ShouldNotBeEmpty();
        controls.ShouldAllBe(control => control.HasAttribute("readonly") || control.HasAttribute("disabled"));
        editor.FindAll("fluent-button").ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void TextTakesTheLocaleAndDirectionOfTheContentOnItsInputOnly(string pseudo)
    {
        Begin(pseudo);
        JsonNode? changed = null;
        var editor = RenderEditor(Field("title") with { Validation = new FieldValidationDescription { MaxLength = 200 } }, JsonValue.Create("مرحبا"), changed: value => changed = value, locale: "ar");

        var input = editor.Find("input");
        (input.GetAttribute("lang"), input.GetAttribute("dir")).ShouldBe(("ar", "rtl"));
        editor.Find("label").Closest("[lang]").ShouldBeNull();
        editor.Find(".cms-field-counter").Closest("[dir]").ShouldBeNull();
        input.Input("سلام");

        changed!.GetValue<string>().ShouldBe("سلام");
    }

    [Fact]
    public void EmptyTextIsNoValue()
    {
        Begin(PseudoCulture.LeftToRight);
        JsonNode? changed = JsonValue.Create("x");
        var editor = RenderEditor(Field("title"), JsonValue.Create("x"), changed: value => changed = value);

        editor.Find("input").Input("");

        changed.ShouldBeNull();
    }

    [Fact]
    public void TheFormSaysWhetherAValueIsRequired()
    {
        Begin(PseudoCulture.LeftToRight);
        var editor = RenderEditor(Field("title"), parameters: new() { [nameof(FieldEditorBase.Required)] = true });

        editor.Find("input").GetAttribute("aria-required").ShouldBe("true");
    }

    [Theory]
    [InlineData("email", "email", "name@", "ada@example.com")]
    [InlineData("url", "url", "example.com", "https://example.com/a")]
    public void AnEmailOrUrlIsLeftToRightWithItsKeyboardAndItsFormIsCheckedAsTheUserLeaves(string type, string inputType, string wrong, string right)
    {
        Begin(PseudoCulture.RightToLeft);
        var left = 0;
        JsonNode? value = null;
        var editor = RenderEditor(FieldOf(type), changed: changed => value = changed, locale: "ar", parameters: new() { [nameof(FieldEditorBase.OnBlur)] = EventCallback.Factory.Create(this, () => left++) });

        var input = editor.Find("input");
        (input.GetAttribute("type"), input.GetAttribute("dir"), input.GetAttribute("autocomplete")).ShouldBe((inputType, "ltr", "off"));
        input.HasAttribute("lang").ShouldBeFalse();

        input.Input(wrong);
        editor.FindAll("[aria-invalid=true]").ShouldBeEmpty();
        editor.Render(p => p.Add(x => x.Value, value));
        editor.Find("input").Blur();

        left.ShouldBe(1);
        editor.Find("input").GetAttribute("aria-invalid").ShouldBe("true");

        editor.Find("input").Input(right);

        editor.FindAll("[aria-invalid=true]").ShouldBeEmpty();
        value!.GetValue<string>().ShouldBe(right);
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void ANumberIsShownAndEnteredInTheUsersFormatAndSentAsAJsonNumber(string pseudo)
    {
        Begin(pseudo);
        JsonNode? value = null;
        var editor = RenderEditor(FieldOf("decimal"), JsonValue.Create(1234.5m), changed: changed => value = changed);
        var separator = Formats.NumberCulture.NumberFormat.NumberDecimalSeparator;

        editor.Find("input").GetAttribute("value").ShouldBe($"1234{separator}5");
        editor.Find("input").Change($"-12{separator}75");

        value!.ToJsonString().ShouldBe("-12.75");
    }

    [Fact]
    public void AnIntegerIsSentAsAWholeNumberAndARangeFromZeroAsksForTheNumericKeyboard()
    {
        Begin(PseudoCulture.LeftToRight);
        JsonNode? value = null;
        var editor = RenderEditor(FieldOf("integer") with { Validation = new FieldValidationDescription { Minimum = 0 } }, changed: changed => value = changed);

        editor.Find("input").GetAttribute("inputmode").ShouldBe("numeric");
        editor.Find("input").Change("42");

        value!.ToJsonString().ShouldBe("42");
    }

    [Fact]
    public void ABooleanIsASwitch()
    {
        Begin(PseudoCulture.LeftToRight);
        JsonNode? value = null;
        var editor = RenderEditor(FieldOf("boolean"), changed: changed => value = changed);

        var toggle = editor.FindComponent<CmsSwitch>();
        toggle.Instance.Value.ShouldBeFalse();
        toggle.InvokeAsync(() => toggle.Instance.ValueChanged.InvokeAsync(true));

        value!.GetValue<bool>().ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void ADateIsShownAndEnteredInTheUsersFormatAndSentAsAnIsoDate(string pseudo)
    {
        Begin(pseudo);
        JsonNode? value = null;
        var editor = RenderEditor(FieldOf("date"), JsonValue.Create("2026-11-15"), changed: changed => value = changed);

        editor.Find("input").GetAttribute("value").ShouldBe(new DateOnly(2026, 11, 15).ToString("d", Formats.Culture));
        editor.Find("input").Change(new DateOnly(2027, 2, 3).ToString("d", Formats.Culture));

        value!.GetValue<string>().ShouldBe("2027-02-03");
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void ADateTimeIsShownAndEnteredInTheUsersTimeZoneAndSentInUtc(string pseudo)
    {
        Begin(pseudo);
        Formats.Apply(null, null, "Asia/Tokyo");
        JsonNode? value = null;
        var editor = RenderEditor(FieldOf("dateTime"), JsonValue.Create("2026-11-15T20:00:00Z"), changed: changed => value = changed);

        var inputs = editor.FindAll("input");
        inputs[0].GetAttribute("value").ShouldBe(new DateOnly(2026, 11, 16).ToString("d", Formats.Culture));
        inputs[1].GetAttribute("value").ShouldBe(new TimeOnly(5, 0).ToString(Formats.Culture.DateTimeFormat.ShortTimePattern, Formats.Culture));
        editor.Find(".cms-field-message").TextContent.ShouldBe(Text("Content.Field.TimeZone", "Asia/Tokyo"));

        inputs[1].Change(new TimeOnly(6, 30).ToString(Formats.Culture.DateTimeFormat.ShortTimePattern, Formats.Culture));

        value!.GetValue<string>().ShouldBe("2026-11-15T21:30:00Z");
    }

    [Fact]
    public void ADateTimeWithSecondsKeepsThemAndATimeTheClockSkipsIsRefused()
    {
        Begin(PseudoCulture.LeftToRight);
        Formats.Apply(null, null, "Europe/Berlin");
        var raised = 0;
        var editor = RenderEditor(FieldOf("dateTime"), JsonValue.Create("2026-03-29T00:15:30Z"), changed: _ => raised++);

        editor.FindAll("input")[1].GetAttribute("value").ShouldBe(new TimeOnly(1, 15, 30).ToString(Formats.Culture.DateTimeFormat.LongTimePattern, Formats.Culture));

        editor.FindAll("input")[1].Change(new TimeOnly(2, 30, 0).ToString(Formats.Culture.DateTimeFormat.LongTimePattern, Formats.Culture));

        raised.ShouldBe(0);
        editor.Find(".cms-field-message-error").TextContent.ShouldBe(Text("Content.Field.TimeSkipped"));
    }

    [Fact]
    public void ADateWithoutATimeIsAtMidnightAndClearingTheDateClearsTheValue()
    {
        Begin(PseudoCulture.LeftToRight);
        JsonNode? value = JsonValue.Create("x");
        var editor = RenderEditor(FieldOf("dateTime"), changed: changed => value = changed);

        editor.FindAll("input")[0].Change(new DateOnly(2026, 5, 1).ToString("d", Formats.Culture));
        value!.GetValue<string>().ShouldBe("2026-05-01T00:00:00Z");

        editor.Render(p => p.Add(x => x.Value, value));
        editor.FindAll("input")[0].Change("");

        value.ShouldBeNull();
    }

    [Fact]
    public void ASingleSelectIsADropdownOfItsOptionsThatAnOptionalFieldCanClear()
    {
        Begin(PseudoCulture.LeftToRight);
        JsonNode? value = JsonValue.Create("news");
        var editor = RenderEditor(Select(false, "news", "video"), value, changed: changed => value = changed);

        editor.FindAll("option").Select(option => option.GetAttribute("value")).ShouldBe(["", "news", "video"]);
        editor.Find("select").Change("video");
        value!.GetValue<string>().ShouldBe("video");

        editor.Find("select").Change("");
        value.ShouldBeNull();
    }

    [Fact]
    public void ARequiredSelectWithAValueCannotBeCleared()
    {
        Begin(PseudoCulture.LeftToRight);
        var editor = RenderEditor(Select(false, "news", "video"), JsonValue.Create("news"), parameters: new() { [nameof(FieldEditorBase.Required)] = true });

        editor.FindAll("option").Select(option => option.GetAttribute("value")).ShouldBe(["news", "video"]);
    }

    [Fact]
    public void AMultipleSelectIsACheckboxPerOptionAndSendsTheValuesInTheOrderOfTheOptions()
    {
        Begin(PseudoCulture.LeftToRight);
        JsonNode? value = new JsonArray("c");
        var editor = RenderEditor(Select(true, "a", "b", "c"), value, changed: changed => value = changed);

        editor.Find("fieldset").ClassList.ShouldNotContain("field-choices-many");
        var boxes = editor.FindComponents<CmsCheckbox>();
        boxes.Select(box => box.Instance.Label).ShouldBe(["a", "b", "c"]);
        boxes[0].InvokeAsync(() => boxes[0].Instance.ValueChanged.InvokeAsync(true));

        value!.ToJsonString().ShouldBe("""["a","c"]""");
    }

    [Fact]
    public void FromFiveOptionsTheCheckboxesScroll()
    {
        Begin(PseudoCulture.LeftToRight);
        var editor = RenderEditor(Select(true, "a", "b", "c", "d", "e"));

        editor.Find("fieldset").ClassList.ShouldContain("field-choices-many");
    }

    [Fact]
    public void ASlugFollowsItsSourceUntilItIsTypedInto()
    {
        Begin(PseudoCulture.LeftToRight);
        var document = new JsonObject { ["title"] = "" };
        JsonNode? value = null;
        var editor = RenderEditor(Slug(), document: document, changed: changed => value = changed);

        document["title"] = "Hello World";
        editor.Render(p => p.Add(x => x.Document, document));
        value!.GetValue<string>().ShouldBe("hello-world");

        document["title"] = "Crème brûlée, again!";
        editor.Render(p => p.Add(x => x.Value, value).Add(x => x.Document, document));
        value!.GetValue<string>().ShouldBe("creme-brulee-again");

        editor.Find("input").Input("my-own");
        document["title"] = "Something else";
        editor.Render(p => p.Add(x => x.Value, value).Add(x => x.Document, document));

        value!.GetValue<string>().ShouldBe("my-own");
    }

    [Fact]
    public void ASlugIsKeptWhileLockedAndFollowsAgainOnceUnlocked()
    {
        Begin(PseudoCulture.LeftToRight);
        var document = new JsonObject { ["title"] = "" };
        JsonNode? value = null;
        var editor = RenderEditor(Slug(), document: document, changed: changed => value = changed);
        var keep = editor.FindComponent<CmsSwitch>();

        keep.Instance.Value.ShouldBeFalse();
        keep.InvokeAsync(() => keep.Instance.ValueChanged.InvokeAsync(true));
        document["title"] = "Second";
        editor.Render(p => p.Add(x => x.Value, value).Add(x => x.Document, document));
        value.ShouldBeNull();

        keep = editor.FindComponent<CmsSwitch>();
        keep.InvokeAsync(() => keep.Instance.ValueChanged.InvokeAsync(false));

        value!.GetValue<string>().ShouldBe("second");
    }

    // A published URL does not move with a new title, whether or not the slug was made from the old one.
    [Theory]
    [InlineData("hello")]
    [InlineData("custom")]
    public void AnEntryWithASlugKeepsItWhenItsSourceChanges(string slug)
    {
        Begin(PseudoCulture.LeftToRight);
        var raised = false;
        var document = new JsonObject { ["title"] = "Hello" };
        var editor = RenderEditor(Slug(), JsonValue.Create(slug), document: document, changed: _ => raised = true);

        document["title"] = "Hello again";
        editor.Render(p => p.Add(x => x.Document, document));

        raised.ShouldBeFalse();
        editor.FindComponent<CmsSwitch>().Instance.Value.ShouldBeTrue();
    }

    [Fact]
    public void ANewEntryWhoseSourceHasADefaultIsNotChangedByItsFirstRender()
    {
        Begin(PseudoCulture.LeftToRight);
        var raised = false;

        RenderEditor(Slug(), document: new JsonObject { ["title"] = "Default title" }, changed: _ => raised = true);

        raised.ShouldBeFalse();
    }

    [Fact]
    public void TheDocumentIsReadOnly()
    {
        Begin(PseudoCulture.LeftToRight);
        var editor = RenderEditor(Slug(), document: new JsonObject { ["title"] = "x" });

        editor.FindComponent<SlugFieldEditor>().Instance.Document.ShouldBeAssignableTo<IReadOnlyDictionary<string, JsonNode?>>();
        (editor.FindComponent<SlugFieldEditor>().Instance.Document as IDictionary<string, JsonNode?>)!.IsReadOnly.ShouldBeTrue();
    }

    [Theory]
    [InlineData("""{"a":1,"a":2}""")]
    [InlineData("""{"x":"\udc00"}""")]
    public void JsonThatTheServerWouldRefuseIsNotSentAndDoesNotBreakTheEditor(string typed)
    {
        Begin(PseudoCulture.LeftToRight);
        var raised = 0;
        var editor = RenderEditor(FieldOf("json"), new JsonObject { ["a"] = 1 }, changed: _ => raised++);

        editor.Find("textarea").Input(typed);
        editor.Render(p => p.Add(x => x.Value, new JsonObject { ["a"] = 1 }));

        raised.ShouldBe(0);
        editor.Find("textarea").GetAttribute("aria-invalid").ShouldBe("true");
        editor.Find(".cms-field-message-error").TextContent.ShouldBeOneOf(Text("Content.Field.JsonInvalid", 1), Text("Content.Field.JsonInvalidNoLine"));
    }

    [Fact]
    public void AChosenValueTheOptionsNoLongerListIsShownAndCanBeTakenOff()
    {
        Begin(PseudoCulture.LeftToRight);
        JsonNode? value = new JsonArray("a", "gone");
        var editor = RenderEditor(Select(true, "a", "b"), value, changed: changed => value = changed);

        var boxes = editor.FindComponents<CmsCheckbox>();
        boxes.Select(box => box.Instance.Label).ShouldBe(["a", "b", Text("Content.Field.NotAnOption", "gone")]);
        boxes[2].Instance.Value.ShouldBeTrue();
        boxes[2].InvokeAsync(() => boxes[2].Instance.ValueChanged.InvokeAsync(false));

        value!.ToJsonString().ShouldBe("""["a"]""");
    }

    [Fact]
    public void ASingleValueTheOptionsNoLongerListIsShownAsItsOption()
    {
        Begin(PseudoCulture.LeftToRight);
        var editor = RenderEditor(Select(false, "a", "b"), JsonValue.Create("gone"));

        editor.FindAll("option").Select(option => option.TextContent).ShouldBe([Services.GetRequiredService<ICmsTextSource>().GetText("SelectNone"), "a", "b", Text("Content.Field.NotAnOption", "gone")]);
        editor.Find("option[selected]").GetAttribute("value").ShouldBe("gone");
    }

    [Fact]
    public async Task FocusMovingInsideADateTimeIsNoBlurAndLeavingItIs()
    {
        Begin(PseudoCulture.LeftToRight);
        var left = 0;
        var editor = RenderEditor(FieldOf("dateTime"), parameters: new() { [nameof(FieldEditorBase.OnBlur)] = EventCallback.Factory.Create(this, () => left++) });

        // As the browser does: focusout on one input and focusin on the next come in one task, before any timer of the page
        // runs. Two separate dispatches would let the timer of the focusout run between them on a busy machine.
        var fieldset = editor.Find("fieldset");
        await editor.InvokeAsync(async () =>
        {
            var leaving = fieldset.FocusOutAsync(new FocusEventArgs());
            await fieldset.FocusInAsync(new FocusEventArgs());
            await leaving;
        });
        left.ShouldBe(0);

        editor.Find("fieldset").FocusOut();

        editor.WaitForAssertion(() => left.ShouldBe(1));
    }

    [Fact]
    public void LeavingTheCheckboxesOfAMultipleSelectIsOneBlur()
    {
        Begin(PseudoCulture.LeftToRight);
        var left = 0;
        var editor = RenderEditor(Select(true, "a", "b"), parameters: new() { [nameof(FieldEditorBase.OnBlur)] = EventCallback.Factory.Create(this, () => left++) });

        editor.Find("fieldset").FocusOut();

        editor.WaitForAssertion(() => left.ShouldBe(1));
    }

    [Fact]
    public void JsonThatDoesNotParseNamesItsLineAndIsNotSent()
    {
        Begin(PseudoCulture.LeftToRight);
        var raised = 0;
        JsonNode? value = null;
        var editor = RenderEditor(FieldOf("json"), new JsonObject { ["a"] = 1 }, changed: changed => { value = changed; raised++; });

        editor.Find("textarea").GetAttribute("dir").ShouldBe("ltr");
        editor.Find("textarea").GetAttribute("spellcheck").ShouldBe("false");
        editor.Find("textarea").Input("{\n  \"a\": 1,\n  oops\n}");

        raised.ShouldBe(0);
        editor.Find("textarea").GetAttribute("aria-invalid").ShouldBe("true");
        editor.Find(".cms-field-message-error").TextContent.ShouldBe(Text("Content.Field.JsonInvalid", 3));

        editor.Find("textarea").Input("""{"a":[1,2]}""");

        raised.ShouldBe(1);
        value!.ToJsonString().ShouldBe("""{"a":[1,2]}""");
        editor.FindAll("[aria-invalid=true]").ShouldBeEmpty();
    }

    [Fact]
    public void FormatIndentsTheJson()
    {
        Begin(PseudoCulture.LeftToRight);
        var editor = RenderEditor(FieldOf("json"), changed: _ => { });

        editor.Find("textarea").Input("""{"a":[1,2]}""");
        var format = editor.FindComponent<CmsButton>();
        format.InvokeAsync(() => format.Instance.OnClick.InvokeAsync());

        editor.Find("textarea").GetAttribute("value").ShouldBe(new JsonObject { ["a"] = new JsonArray(1, 2) }.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true, NewLine = "\n" }));
    }

    [Fact]
    public void ALongTextGrowsAndCounts()
    {
        Begin(PseudoCulture.LeftToRight);
        var editor = RenderEditor(FieldOf("longText") with { Validation = new FieldValidationDescription { MaxLength = 500 } }, JsonValue.Create("abc"), locale: "en");

        editor.Find("textarea").ClassList.ShouldContain("cms-textarea-grow");
        editor.Find("textarea").GetAttribute("lang").ShouldBe("en");
        editor.Find(".cms-field-counter").ShouldNotBeNull();
    }

    [Fact]
    public void LeavingAnEditorRaisesOnBlur()
    {
        Begin(PseudoCulture.LeftToRight);
        var left = 0;
        foreach (var type in new[] { "text", "longText", "integer", "date", "select", "slug", "json" })
        {
            RenderEditor(FieldOf(type), parameters: new() { [nameof(FieldEditorBase.OnBlur)] = EventCallback.Factory.Create(this, () => left++) })
                .FindAll("input, textarea, select")[0].Blur();
        }

        left.ShouldBe(7);
    }

    [Theory]
    [InlineData("Hello, World!", null, "hello-world")]
    [InlineData("  Crème brûlée  ", null, "creme-brulee")]
    [InlineData("Straße & Œuvre", null, "strasse-oeuvre")]
    [InlineData("Привет мир", null, "привет-мир")]
    [InlineData("مـرحبا بالعالم", null, "مرحبا-بالعالم")]
    [InlineData("one two three", 7, "one-two")]
    [InlineData("!!!", null, "")]
    public void ASlugIsMadeAsTheServerMakesIt(string source, int? maxLength, string slug) =>
        Slugs.Slugify(source, maxLength).ShouldBe(slug);

    private void Begin(string pseudo)
    {
        culture = CultureInfo.GetCultureInfo(pseudo);
        UseInterfaceCulture(pseudo);
        Start();
    }

    // Data the fake would serve, marked as a resource is, so that the check of the texts sees what the editor adds.
    private string Data(string text) => PseudoCulture.Transform(text, culture.TextInfo.IsRightToLeft);

    private SchemaFieldDescription FieldOf(string type) => type switch
    {
        "select" => Select(false, Data("news"), Data("video")),
        "multipleSelect" => Select(true, Data("news"), Data("video")),
        "slug" => Slug(),
        _ => Field("field", type),
    };

    private static SchemaFieldDescription Plain(string name) => new() { Name = name, Type = "text", Validation = new FieldValidationDescription() };

    private SchemaFieldDescription Field(string name, string type = "text") => new()
    {
        Name = name,
        Type = type,
        Display = new FieldDisplayDescription { Label = Data("Label of " + name) },
        Validation = new FieldValidationDescription(),
    };

    private SchemaFieldDescription Select(bool multiple, params string[] options) => Field("category", "select") with
    {
        Multiple = multiple,
        Settings = new JsonObject { ["options"] = new JsonArray([.. options.Select(option => (JsonNode)JsonValue.Create(option))]) },
    };

    private SchemaFieldDescription Slug() => Field("slug", "slug") with { Settings = new JsonObject { ["source"] = "title" } };

    private IRenderedComponent<EditorHost> RenderEditor(
        SchemaFieldDescription field,
        JsonNode? value = null,
        Action<JsonNode?>? changed = null,
        bool readOnly = false,
        string? locale = null,
        JsonObject? document = null,
        Dictionary<string, object?>? parameters = null) =>
        Render<EditorHost>(p => p
            .Add(x => x.Editor, Services.GetRequiredService<FieldEditorRegistry>().Find(field.Type)!.Editor)
            .Add(x => x.Field, field)
            .Add(x => x.Value, value)
            .Add(x => x.Changed, changed ?? (_ => { }))
            .Add(x => x.ReadOnly, readOnly)
            .Add(x => x.Locale, locale)
            .Add(x => x.Document, document)
            .Add(x => x.Parameters, parameters ?? []));

    // Renders the registered editor of a field as the form does, so that a test sees what the form gets.
    public sealed class EditorHost : ComponentBase
    {
        [Parameter]
        public Type Editor { get; set; } = null!;

        [Parameter]
        public SchemaFieldDescription Field { get; set; } = null!;

        [Parameter]
        public JsonNode? Value { get; set; }

        [Parameter]
        public Action<JsonNode?> Changed { get; set; } = _ => { };

        [Parameter]
        public bool ReadOnly { get; set; }

        [Parameter]
        public string? Locale { get; set; }

        [Parameter]
        public JsonObject? Document { get; set; }

        [Parameter]
        public Dictionary<string, object?> Parameters { get; set; } = [];

        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
            builder.OpenComponent(0, Editor);
            builder.AddComponentParameter(1, nameof(FieldEditorBase.Field), Field);
            builder.AddComponentParameter(2, nameof(FieldEditorBase.Value), Value);
            builder.AddComponentParameter(3, nameof(FieldEditorBase.ValueChanged), EventCallback.Factory.Create<JsonNode?>(this, value =>
            {
                Value = value;
                Changed(value);
            }));
            builder.AddComponentParameter(4, nameof(FieldEditorBase.ReadOnly), ReadOnly);
            builder.AddComponentParameter(5, nameof(FieldEditorBase.ContentLocale), Locale);
            builder.AddComponentParameter(6, nameof(FieldEditorBase.TextDirection), Locale is null ? FieldEditorBase.LeftToRight : TextDirection.Of(Locale));
            builder.AddComponentParameter(7, nameof(FieldEditorBase.Document), Document is null ? null : new System.Collections.ObjectModel.ReadOnlyDictionary<string, JsonNode?>(Document));
            var sequence = 8;
            foreach (var (name, parameter) in Parameters)
            {
                builder.AddComponentParameter(sequence++, name, parameter);
            }

            builder.CloseComponent();
        }
    }
}
