namespace FluentCMS.Schema;

/// <summary>Hints that tell the admin how to present a field. They do not affect storage.</summary>
public sealed class FieldDisplay
{
    internal FieldDisplay()
    {
    }

    /// <summary>The label, or <see langword="null"/> to derive it from the field name.</summary>
    public string? Label { get; internal init; }

    /// <summary>The help text shown beside the field.</summary>
    public string? Help { get; internal init; }

    /// <summary>The placeholder shown in an empty input.</summary>
    public string? Placeholder { get; internal init; }

    /// <summary>The group of fields this field is shown with.</summary>
    public string? Group { get; internal init; }

    /// <summary>The tab this field is shown on.</summary>
    public string? Tab { get; internal init; }

    /// <summary>The width in columns of a twelve-column grid, or 0 to leave it to the admin.</summary>
    public int Width { get; internal init; }
}
