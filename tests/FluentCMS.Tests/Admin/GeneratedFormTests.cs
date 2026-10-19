using System.Text.Json;
using Bunit;
using FluentCMS.Admin.Components.Forms;
using FluentCMS.Admin.Forms;
using FluentCMS.Core.Schema;

namespace FluentCMS.Tests.Admin;

public sealed class GeneratedFormTests
{
    [Theory]
    [InlineData("text", "{}", " Hello ", "\" Hello \"")]
    [InlineData("richText", "{}", "A **b**", "\"A **b**\"")]
    [InlineData("number", "{}", "129.5", "129.5")]
    [InlineData("number", "{}", "-3", "-3")]
    [InlineData("boolean", "{}", "true", "true")]
    [InlineData("dateTime", "{}", "2026-11-01T09:30", "\"2026-11-01T09:30:00Z\"")]
    [InlineData("dateTime", """{"dateOnly": true}""", "2026-11-02", "\"2026-11-02\"")]
    [InlineData("enum", """{"values": ["red"]}""", "red", "\"red\"")]
    [InlineData("list", """{"itemType": "text"}""", """["a", "b"]""", """["a","b"]""")]
    [InlineData("json", "{}", """{"k": 1}""", """{"k":1}""")]
    public void A_posted_text_becomes_the_fields_json_value(string type, string settings, string? posted, string json)
    {
        var value = FieldValues.Read(Field(type, settings), posted, out var problem);

        Assert.Null(problem);
        Assert.Equal(json, value!.ToJsonString());
    }

    [Theory]
    [InlineData("number", "{}", "cheap", "number")]
    [InlineData("dateTime", "{}", "soon", "dateTime")]
    [InlineData("dateTime", """{"dateOnly": true}""", "31/12/2026", "date")]
    [InlineData("json", "{}", "{", "json")]
    [InlineData("list", """{"itemType": "text"}""", "[1,", "json")]
    public void A_text_that_cannot_be_the_value_is_a_problem(string type, string settings, string posted, string expected)
    {
        Assert.Null(FieldValues.Read(Field(type, settings), posted, out var problem));
        Assert.Equal(expected, problem);
    }

    [Theory]
    [InlineData("text")]
    [InlineData("number")]
    [InlineData("reference")]
    public void An_empty_field_has_no_value(string type)
    {
        Assert.Null(FieldValues.Read(Field(type, "{}"), "  ", out var problem));
        Assert.Null(problem);
    }

    [Theory]
    [InlineData("dateTime", "{}", "\"2026-11-01T09:30:45.5+02:00\"", "2026-11-01T07:30")]
    [InlineData("number", "{}", "4.50", "4.50")]
    [InlineData("text", "{}", "\"one\\ntwo\"", "one\r\ntwo")]
    [InlineData("json", "{}", """{"k":[1,2]}""", "{\r\n  \"k\": [\r\n    1,\r\n    2\r\n  ]\r\n}")]
    [InlineData("boolean", "{}", "true", "true")]
    public void A_posted_text_that_shows_as_the_stored_value_keeps_it_exactly(string type, string settings, string stored, string posted)
    {
        var value = FieldValues.Read(Field(type, settings), posted, out var problem, JsonDocument.Parse(stored).RootElement);

        Assert.Null(problem);
        Assert.Equal(stored, value!.ToJsonString(Unescaped));
    }

    [Fact]
    public void A_changed_text_replaces_the_stored_value()
    {
        var value = FieldValues.Read(Field("dateTime", "{}"), "2026-11-01T07:31", out _, JsonDocument.Parse("\"2026-11-01T09:30:45+02:00\"").RootElement);

        Assert.Equal("\"2026-11-01T07:31:00Z\"", value!.ToJsonString());
    }

    [Theory]
    [InlineData(null, false, null)]
    [InlineData("true", false, "false")]
    [InlineData(null, true, "false")]
    public void An_unchecked_box_is_false_only_when_the_field_is_required_or_had_a_value(string? stored, bool required, string? expected)
    {
        var storedValue = stored is null ? (JsonElement?)null : JsonDocument.Parse(stored).RootElement;

        var value = FieldValues.Read(Field("boolean", "{}", required), null, out var problem, storedValue);

        Assert.Null(problem);
        Assert.Equal(expected, value?.ToJsonString());
    }

    [Theory]
    [InlineData("dateTime", "{}", "\"2026-11-01T11:30:00+02:00\"", "2026-11-01T09:30")]
    [InlineData("boolean", "{}", "true", "true")]
    [InlineData("boolean", "{}", "false", null)]
    [InlineData("number", "{}", "4.50", "4.50")]
    [InlineData("text", "{}", "\"Hello\"", "Hello")]
    [InlineData("json", "{}", """{"k":"ä"}""", "{\n  \"k\": \"ä\"\n}")]
    public void A_stored_value_shows_as_its_widget_takes_it(string type, string settings, string stored, string? shown)
    {
        Assert.Equal(shown, FieldValues.Show(Field(type, settings), JsonDocument.Parse(stored).RootElement)?.ReplaceLineEndings("\n"));
    }

    [Theory]
    [InlineData("text", "input[type=text]")]
    [InlineData("richText", "textarea")]
    [InlineData("number", "input[type=number]")]
    [InlineData("boolean", "input[type=checkbox]")]
    [InlineData("dateTime", "input[type=datetime-local]")]
    [InlineData("enum", "select")]
    [InlineData("reference", "select")]
    [InlineData("media", "select")]
    [InlineData("list", "textarea")]
    [InlineData("json", "textarea")]
    public void Each_field_type_renders_its_widget_inside_a_form_field(string type, string selector)
    {
        // Before the circuit attaches, a reference or media picker is its plain select.
        using var context = PickerFieldTests.Context(interactive: false);

        var cut = context.Render<FieldWidget>(widget => widget
            .Add(w => w.Field, Field(type, type == "enum" ? """{"values": ["red", "blue"]}""" : "{}", required: true))
            .Add(w => w.Options, [new FieldOption("0192f3a1-7c2e-7a4b-9d3e-1f2a3b4c5d6e", "Tools")])
            .Add(w => w.Error, "Wrong."));

        var control = cut.Find(selector);
        Assert.Equal("Form.Values[field]", control.GetAttribute("name"));
        Assert.Equal(cut.Find("label").GetAttribute("for"), control.GetAttribute("id"));
        Assert.Contains("(required)", cut.Find("label").TextContent);
        Assert.Contains("Wrong.", cut.Find("[data-slot=form-message]").TextContent);
    }

    [Fact]
    public void An_enum_offers_its_values_and_marks_the_stored_one()
    {
        using var context = new AdminTestContext();

        var cut = context.Render<FieldWidget>(widget => widget
            .Add(w => w.Field, Field("enum", """{"values": ["red", "blue"]}"""))
            .Add(w => w.Value, "blue"));

        Assert.Equal(["", "red", "blue"], cut.FindAll("option").Select(option => option.GetAttribute("value")));
        Assert.True(cut.Find("option[value=blue]").HasAttribute("selected"));
    }

    private static readonly JsonSerializerOptions Unescaped = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static FieldDefinition Field(string type, string settings, bool required = false)
    {
        return new FieldDefinition("field", type, required, JsonDocument.Parse(settings).RootElement);
    }
}
