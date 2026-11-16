using System.Text.Json.Nodes;

namespace FluentCMS.Admin.Api;

/// <summary>A field of a <see cref="SchemaTypeDescription"/>.</summary>
public sealed record SchemaFieldDescription
{
    /// <summary>The name of the field in the API, in camelCase.</summary>
    public required string Name { get; init; }

    /// <summary>The field type, such as <c>text</c>, <c>richText</c> or a plugin's <c>media</c>.</summary>
    public required string Type { get; init; }

    /// <summary>Whether the field holds a list.</summary>
    public bool Multiple { get; init; }

    /// <summary>Whether the value differs per locale.</summary>
    public bool Localizable { get; init; }

    /// <summary>Whether the field has a database index.</summary>
    public bool Indexed { get; init; }

    /// <summary>Whether the field is included in full-text search.</summary>
    public bool Searchable { get; init; }

    /// <summary>Whether clients cannot change the value.</summary>
    public bool ReadOnly { get; init; }

    /// <summary>Whether the field is left out of responses and forms.</summary>
    public bool Hidden { get; init; }

    /// <summary>The earlier names of the field.</summary>
    public IReadOnlyList<string> FormerNames { get; init; } = [];

    /// <summary>Hints for the admin, or <see langword="null"/> if the field has none.</summary>
    public FieldDisplayDescription? Display { get; init; }

    /// <summary>The value a new entry starts with, or <see langword="null"/>.</summary>
    public JsonNode? DefaultValue { get; init; }

    /// <summary>The declarative rules a value must meet.</summary>
    public required FieldValidationDescription Validation { get; init; }

    /// <summary>When the field is shown, or <see langword="null"/> if it always is.</summary>
    public ConditionDescription? VisibleWhen { get; init; }

    /// <summary>When the field is required beyond <see cref="FieldValidationDescription.Required"/>, or <see langword="null"/>.</summary>
    public ConditionDescription? RequiredWhen { get; init; }

    /// <summary>The target of a relation field; <see langword="null"/> for other field types.</summary>
    public FieldRelationDescription? Relation { get; init; }

    /// <summary>The settings of the field type, such as the <c>options</c> of a select.</summary>
    public JsonObject Settings { get; init; } = [];
}
