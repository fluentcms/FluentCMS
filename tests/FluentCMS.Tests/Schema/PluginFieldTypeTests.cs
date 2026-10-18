using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core;
using FluentCMS.Core.Extensions;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Tests.Schema;

// A field type outside Core goes through the same doors as a built-in: discovery, the registry, a definition and a list (EXT-N1).
public sealed class PluginFieldTypeTests
{
    [Fact]
    public void A_field_type_from_another_assembly_validates_in_a_definition_like_a_built_in()
    {
        using var provider = new ServiceCollection()
            .AddFluentCmsCore()
            .AddFluentCmsExtensions(typeof(RatingFieldType).Assembly)
            .BuildServiceProvider();
        var definition = new ContentTypeDefinition(
        [
            new FieldDefinition("title", "text", required: true),
            new FieldDefinition("rating", "sample.rating", settings: Settings("""{"stars": 5}""")),
            new FieldDefinition("ratings", "list", settings: Settings("""{"itemType": "sample.rating", "itemSettings": {"stars": 10}}""")),
        ]);

        var result = definition.Validate(provider.GetRequiredService<FieldTypeRegistry>());

        Assert.True(result.IsSuccess, string.Join("; ", result.Errors.Select(error => error.Message)));
        var descriptor = Assert.Single(provider.GetRequiredService<IExtensionCatalog>().For<IFieldType>(), d => d.Implementation == typeof(RatingFieldType));
        Assert.False(descriptor.IsBuiltIn);
    }

    [Fact]
    public void A_list_checks_a_plugin_item_type_through_the_registry()
    {
        using var provider = new ServiceCollection()
            .AddFluentCmsCore()
            .AddFluentCmsExtensions(typeof(RatingFieldType).Assembly)
            .BuildServiceProvider();

        var error = Assert.Single(provider.GetRequiredService<FieldTypeRegistry>()
            .ValidateSettings("list", Settings("""{"itemType": "sample.rating", "itemSettings": {"stars": 11}}""")));

        Assert.Equal("In the list items: The setting 'stars' must be at most 10.", error.Message);
    }

    [Fact]
    public void Without_its_assembly_the_type_is_unknown()
    {
        using var provider = new ServiceCollection().AddFluentCmsCore().BuildServiceProvider();
        var definition = new ContentTypeDefinition([new FieldDefinition("rating", "sample.rating")]);

        var error = Assert.Single(definition.Validate(provider.GetRequiredService<FieldTypeRegistry>()).Errors);

        Assert.Equal(SchemaErrorCodes.FieldTypeUnknown, error.Code);
    }

    private static JsonElement Settings(string json)
    {
        return JsonSerializer.Deserialize<JsonElement>(json);
    }
}

public sealed class RatingFieldTypeTests : FieldTypeConformanceTests
{
    protected override string FieldTypeName => "sample.rating";

    protected override IEnumerable<string> AcceptedSettings => ["{}", """{"stars": 5}"""];

    protected override IEnumerable<string> RejectedSettings => ["""{"stars": 0}""", """{"stars": 11}"""];

    protected override IEnumerable<(string Settings, string Value)> AcceptedValues =>
    [
        ("{}", "5"),
        ("""{"stars": 10}""", "1"),
    ];

    protected override IEnumerable<(string Settings, string Value)> RejectedValues =>
    [
        ("{}", "6"),
        ("""{"stars": 10}""", "0"),
        ("{}", "\"5\""),
    ];

    protected override void AddFieldTypes(IServiceCollection services)
    {
        services.AddFluentCmsExtensions(typeof(RatingFieldType).Assembly);
    }
}

// What a plugin field type looks like: outside FluentCMS.Core.Builtin, using only what Core makes public.
public sealed class RatingFieldType : IFieldType
{
    public string Name => "sample.rating";

    public IReadOnlyList<Error> ValidateSettings(JsonElement settings, FieldTypeRegistry fieldTypes)
    {
        var reader = new FieldSettingsReader(Name, settings);

        if (reader.PositiveInteger("stars") > 10)
        {
            reader.Invalid("stars", "must be at most 10");
        }

        return reader.Errors();
    }

    public IReadOnlyList<Error> ValidateValue(JsonElement value, JsonElement settings, FieldTypeRegistry fieldTypes)
    {
        var stars = new FieldSettingsReader(Name, settings).PositiveInteger("stars") ?? 5;

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var rating) && rating >= 1 && rating <= stars
            ? []
            : [new Error(SchemaErrorCodes.ValueInvalid, $"The value must be a whole number from 1 to {stars}.")];
    }

    public FieldValueDescription DescribeValue(JsonElement settings, FieldTypeRegistry fieldTypes)
    {
        var stars = new FieldSettingsReader(Name, settings).PositiveInteger("stars") ?? 5;

        return new FieldValueDescription(
            new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = stars },
            stars,
            $"A rating from 1 to {stars} stars.");
    }
}

public sealed class LinkFieldTypeTests : FieldTypeConformanceTests
{
    protected override string FieldTypeName => "sample.link";

    protected override IEnumerable<string> AcceptedSettings => ["""{"to": "product"}"""];

    protected override IEnumerable<string> RejectedSettings => ["{}", """{"to": "Product"}"""];

    protected override IEnumerable<(string Settings, string Value)> AcceptedValues =>
    [
        ("""{"to": "product"}""", "\"0199a000-0000-7000-8000-000000000001\""),
    ];

    protected override IEnumerable<(string Settings, string Value)> RejectedValues =>
    [
        ("""{"to": "product"}""", "\"product-1\""),
    ];

    protected override void AddFieldTypes(IServiceCollection services)
    {
        services.AddFluentCmsExtensions(typeof(LinkFieldType).Assembly);
    }

    // A plugin type that points at entries declares its targets like the built-in reference does, so the same existence check applies.
    [Fact]
    public void Its_targets_count_as_references_of_a_definition()
    {
        var definition = new ContentTypeDefinition(
        [
            new FieldDefinition("seeAlso", "sample.link", settings: JsonSerializer.Deserialize<JsonElement>("""{"to": "product"}""")),
            new FieldDefinition("category", "reference", settings: JsonSerializer.Deserialize<JsonElement>("""{"target": "category"}""")),
        ]);

        Assert.Equal(
            [new FieldReference("seeAlso", new ReferenceTarget("product")), new FieldReference("category", new ReferenceTarget("category"))],
            definition.References(Registry));
    }
}

public sealed class LinkFieldType : IFieldType
{
    public string Name => "sample.link";

    public IReadOnlyList<Error> ValidateSettings(JsonElement settings, FieldTypeRegistry fieldTypes)
    {
        var reader = new FieldSettingsReader(Name, settings);

        if (reader.RequiredString("to") is { } to && !SchemaNames.IsSlug(to))
        {
            reader.Invalid("to", "must be the slug of a content type");
        }

        return reader.Errors();
    }

    public IReadOnlyList<ReferenceTarget> ReferenceTargets(JsonElement settings, FieldTypeRegistry fieldTypes)
    {
        return [new ReferenceTarget(settings.GetProperty("to").GetString()!)];
    }

    public IReadOnlyList<Error> ValidateValue(JsonElement value, JsonElement settings, FieldTypeRegistry fieldTypes)
    {
        return value.ValueKind == JsonValueKind.String && Guid.TryParse(value.GetString(), out _)
            ? []
            : [new Error(SchemaErrorCodes.ValueInvalid, "The value must be the id of an entry.")];
    }

    public FieldValueDescription DescribeValue(JsonElement settings, FieldTypeRegistry fieldTypes)
    {
        return new FieldValueDescription(
            new JsonObject { ["type"] = "string", ["format"] = "uuid" },
            FieldValueDescription.ExampleId,
            $"The id of an entry of '{settings.GetProperty("to").GetString()}' to see as well.");
    }
}
