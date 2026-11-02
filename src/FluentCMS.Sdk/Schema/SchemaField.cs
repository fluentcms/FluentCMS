using System.Collections.ObjectModel;

namespace FluentCMS.Schema;

/// <summary>A field of a <see cref="SchemaType"/>.</summary>
public sealed class SchemaField
{
    internal SchemaField()
    {
    }

    /// <summary>The name of the field in the API, in camelCase.</summary>
    public string Name { get; internal init; } = "";

    /// <summary>The name of the field type, such as <c>text</c> or <c>richText</c>.</summary>
    public string FieldType { get; internal init; } = "";

    /// <summary>Whether the field holds a list: a multi select, a to-many relation, several media or a repeatable component.</summary>
    public bool Multiple { get; internal init; }

    /// <summary>Whether a value is required to publish.</summary>
    public bool Required { get; internal init; }

    /// <summary>Whether the value must be unique among published entries.</summary>
    public bool Unique { get; internal init; }

    /// <summary>Whether the field has a database index.</summary>
    public bool Indexed { get; internal init; }

    /// <summary>Whether the field is included in full-text search.</summary>
    public bool Searchable { get; internal init; }

    /// <summary>Whether clients cannot change the value.</summary>
    public bool ReadOnly { get; internal init; }

    /// <summary>Whether the field is left out of responses and forms.</summary>
    public bool Hidden { get; internal init; }

    /// <summary>Whether the value differs per locale.</summary>
    public bool Localizable { get; internal init; }

    /// <summary>The earlier names of the field, so a rename is not taken for a drop and an add.</summary>
    public IReadOnlyList<string> FormerNames { get; internal init; } = [];

    /// <summary>Hints for the admin, or <see langword="null"/> if the field has none.</summary>
    public FieldDisplay? Display { get; internal init; }

    /// <summary>The value a new entry starts with: <see langword="null"/>, a string, a boolean, a whole number, a decimal or a list of strings.</summary>
    public object? DefaultValue { get; internal init; }

    /// <summary>The least number of characters, if limited.</summary>
    public int? MinLength { get; internal init; }

    /// <summary>The most number of characters, if limited. It also sizes the database column.</summary>
    public int? MaxLength { get; internal init; }

    /// <summary>The least value, if limited.</summary>
    public decimal? Minimum { get; internal init; }

    /// <summary>The greatest value, if limited.</summary>
    public decimal? Maximum { get; internal init; }

    /// <summary>A regular expression the value must match, if any.</summary>
    public string? Pattern { get; internal init; }

    /// <summary>When the field is shown, or <see langword="null"/> if it always is.</summary>
    public Condition? VisibleWhen { get; internal init; }

    /// <summary>When the field is required beyond <see cref="Required"/>, or <see langword="null"/> if never.</summary>
    public Condition? RequiredWhen { get; internal init; }

    /// <summary>The target of a relation field; <see langword="null"/> for other field types.</summary>
    public FieldRelation? Relation { get; internal init; }

    /// <summary>
    /// The settings of the field type, keyed as in <see cref="FieldSettings"/> for the built-in types. Values are
    /// strings, booleans, whole numbers, decimals or lists of strings, ordered by key.
    /// </summary>
    public IReadOnlyDictionary<string, object> Settings { get; internal init; } = ReadOnlyDictionary<string, object>.Empty;
}
