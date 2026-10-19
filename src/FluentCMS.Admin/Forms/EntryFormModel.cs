namespace FluentCMS.Admin.Forms;

// What a generated form posts: one text per field by name, the reason for the change and its sources, and the version an edit read.
public sealed class EntryFormModel
{
    public Dictionary<string, string?> Values { get; set; } = new(StringComparer.Ordinal);

    // For an entity type, each field's source, owner and validity by "field.part" (FactValues).
    public Dictionary<string, string?> Facts { get; set; } = new(StringComparer.Ordinal);

    public string? Rationale { get; set; }

    // One per line, such as a URL or a file name.
    public string? Sources { get; set; }

    public int BaseVersion { get; set; }

    public IReadOnlyList<string> SourceList()
    {
        return (Sources ?? string.Empty).Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }
}

// A choice a reference or media widget offers: the id it posts and what the person reads.
public sealed record FieldOption(string Value, string Label);
