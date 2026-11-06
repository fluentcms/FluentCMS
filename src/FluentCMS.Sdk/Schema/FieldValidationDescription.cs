namespace FluentCMS.Schema;

/// <summary>The declarative rules of a <see cref="SchemaField"/>. A select's allowed values are its <c>options</c> setting.</summary>
public sealed record FieldValidationDescription
{
    /// <summary>Whether a value is required to publish.</summary>
    public bool Required { get; init; }

    /// <summary>Whether the value must be unique among published entries.</summary>
    public bool Unique { get; init; }

    /// <summary>The least number of characters, if limited.</summary>
    public int? MinLength { get; init; }

    /// <summary>The most number of characters, if limited.</summary>
    public int? MaxLength { get; init; }

    /// <summary>The least value, if limited.</summary>
    public decimal? Minimum { get; init; }

    /// <summary>The greatest value, if limited.</summary>
    public decimal? Maximum { get; init; }

    /// <summary>A regular expression the value must match, if any.</summary>
    public string? Pattern { get; init; }
}
