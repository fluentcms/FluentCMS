namespace FluentCMS.Schema;

/// <summary>A <see cref="SchemaField"/> as plain data.</summary>
public sealed record SchemaFieldDescription
{
    /// <summary>The name of the field in the API, in camelCase.</summary>
    public required string Name { get; init; }

    /// <summary>The name of the field type, such as <c>text</c> or <c>richText</c>.</summary>
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

    /// <summary>The earlier names of the field, so a rename is not taken for a drop and an add.</summary>
    public IReadOnlyList<string> FormerNames { get; init; } = [];

    /// <summary>Hints for the admin, or <see langword="null"/> if the field has none.</summary>
    public FieldDisplayDescription? Display { get; init; }

    /// <summary>The value a new entry starts with: <see langword="null"/>, a string, a boolean, a number or a list of strings.</summary>
    public object? DefaultValue { get; init; }

    /// <summary>The declarative rules a value must meet.</summary>
    public required FieldValidationDescription Validation { get; init; }

    /// <summary>When the field is shown, or <see langword="null"/> if it always is.</summary>
    public ConditionDescription? VisibleWhen { get; init; }

    /// <summary>When the field is required beyond <see cref="FieldValidationDescription.Required"/>, or <see langword="null"/> if never.</summary>
    public ConditionDescription? RequiredWhen { get; init; }

    /// <summary>The target of a relation field; <see langword="null"/> for other field types.</summary>
    public FieldRelationDescription? Relation { get; init; }

    /// <summary>
    /// The settings of the field type, keyed as in <see cref="FieldSettings"/> for the built-in types, such as the
    /// <c>blocks</c> a blocks field allows or the <c>options</c> of a select. Values are strings, booleans, numbers or
    /// lists of strings, ordered by key.
    /// </summary>
    public IReadOnlyDictionary<string, object> Settings { get; init; } = new Dictionary<string, object>();
}
