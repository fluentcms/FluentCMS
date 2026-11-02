using System.Text.Json;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Schema.FieldTypes;
using FluentCMS.Schema;
using Shouldly;

namespace FluentCMS.Core.Tests.Schema.FieldTypes;

internal static class TestFields
{
    private static readonly string[] SelectOptions = ["news", "opinion"];

    private static readonly string[] BlockNames = ["hero"];

    public static IFieldType Type(string name) => FieldTypeRegistry.Core.Find(name) ?? throw new InvalidOperationException($"no field type {name}");

    // Builds a field of the given type through the schema builder, with the settings its type needs to be valid.
    public static SchemaField Field(string typeName, Action<IFieldBuilder>? configure = null)
    {
        var model = new SchemaBuilder()
            .AddComponent("seo", type => type.Field("title", "text"))
            .AddBlock("hero", type => type.Field("heading", "text"))
            .AddCollection("author", type => type.Field("name", "text"))
            .AddCollection("sample", type => type.Field("subject", typeName, field =>
            {
                Defaults(typeName, field);
                configure?.Invoke(field);
            }))
            .Build();

        return model.FindContentType("sample")!.Fields[0];
    }

    public static object? Coerce(string typeName, object? value, Action<IFieldBuilder>? configure = null)
    {
        var field = Field(typeName, configure);
        var accepted = Type(typeName).TryCoerce(value, field, out var result, out var message);

        accepted.ShouldBeTrue(message);
        message.ShouldBeNull();
        return result;
    }

    public static string Reject(string typeName, object? value, Action<IFieldBuilder>? configure = null)
    {
        var field = Field(typeName, configure);
        var accepted = Type(typeName).TryCoerce(value, field, out var result, out var message);

        accepted.ShouldBeFalse($"{typeName} accepted {value}");
        result.ShouldBeNull();
        return message.ShouldNotBeNull();
    }

    public static JsonElement Json(string text)
    {
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static void Defaults(string typeName, IFieldBuilder field)
    {
        switch (typeName)
        {
            case "select":
                field.Setting(FieldSettings.Options, SelectOptions);
                break;
            case "component":
                field.Setting(FieldSettings.Component, "seo");
                break;
            case "blocks":
                field.Setting(FieldSettings.Blocks, BlockNames);
                break;
            case "relation":
                field.Relation("author");
                break;
        }
    }
}
