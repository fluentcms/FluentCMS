namespace FluentCMS.Schema;

/// <summary>A <see cref="FieldDisplay"/> as plain data.</summary>
public sealed record FieldDisplayDescription
{
    /// <summary>The label, or <see langword="null"/> to derive it from the field name.</summary>
    public string? Label { get; init; }

    /// <summary>The help text shown beside the field.</summary>
    public string? Help { get; init; }

    /// <summary>The placeholder shown in an empty input.</summary>
    public string? Placeholder { get; init; }

    /// <summary>The group of fields this field is shown with.</summary>
    public string? Group { get; init; }

    /// <summary>The tab this field is shown on.</summary>
    public string? Tab { get; init; }

    /// <summary>The width in columns of a twelve-column grid, or 0 to leave it to the admin.</summary>
    public int Width { get; init; }
}
