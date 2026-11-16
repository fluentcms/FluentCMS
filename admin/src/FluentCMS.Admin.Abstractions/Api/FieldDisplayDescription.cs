namespace FluentCMS.Admin.Api;

/// <summary>The hints of a <see cref="SchemaFieldDescription"/> for the admin's form.</summary>
public sealed record FieldDisplayDescription
{
    /// <summary>The label, if not derived from the name.</summary>
    public string? Label { get; init; }

    /// <summary>The help text under the input.</summary>
    public string? Help { get; init; }

    /// <summary>The placeholder of the input.</summary>
    public string? Placeholder { get; init; }

    /// <summary>The group the field is shown in.</summary>
    public string? Group { get; init; }

    /// <summary>The tab the field is shown on.</summary>
    public string? Tab { get; init; }

    /// <summary>The width of the field in the form's grid.</summary>
    public int Width { get; init; }
}
