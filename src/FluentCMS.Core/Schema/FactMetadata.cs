namespace FluentCMS.Core.Schema;

// What every field of an entity type carries next to its value (SCH-03). The schema document (SCH-06) describes these names and
// entity values are checked against them (F-ENT); a fact without a source is kept and flagged unsourced (ENT-02).
public static class FactMetadata
{
    public const string Value = "value";
    public const string Source = "source";
    public const string Owner = "owner";
    public const string ValidFrom = "validFrom";
    public const string ValidUntil = "validUntil";
    public const string LastVerified = "lastVerified";

    // A source is {"kind": "url", "reference": "https://..."}; the reference is the URL, file, person or system it names.
    public const string SourceKind = "kind";
    public const string SourceReference = "reference";

    public static IReadOnlyList<string> Properties { get; } = [Source, Owner, ValidFrom, ValidUntil, LastVerified];

    public static IReadOnlyList<string> SourceKinds { get; } = ["url", "file", "person", "system"];
}
