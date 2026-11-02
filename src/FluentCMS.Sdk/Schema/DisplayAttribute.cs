namespace FluentCMS.Schema;

/// <summary>Gives the admin hints for presenting a field. They do not affect storage.</summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class DisplayAttribute : Attribute
{
    /// <summary>The label.</summary>
    public string? Label { get; set; }

    /// <summary>The help text shown beside the field.</summary>
    public string? Help { get; set; }

    /// <summary>The placeholder shown in an empty input.</summary>
    public string? Placeholder { get; set; }

    /// <summary>The group of fields the field is shown with.</summary>
    public string? Group { get; set; }

    /// <summary>The tab the field is shown on.</summary>
    public string? Tab { get; set; }

    /// <summary>The width in columns of a twelve-column grid. 0 leaves it to the admin.</summary>
    public int Width { get; set; }
}
