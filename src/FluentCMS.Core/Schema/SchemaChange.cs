namespace FluentCMS.Core.Schema;

internal enum SchemaChangeKind
{
    TypeAdded,
    TypeRemoved,
    TypeChanged,
    FieldAdded,
    FieldRemoved,
    FieldRenamed,
    FieldChanged,
    FieldsReordered,
}

// One difference between two snapshots. Family is "type" for a collection or singleton, else "component" or "block", because
// a component and a block may share a name. Properties are the JSON keys that differ, without their values, which a log
// must not carry (a default value can be anything).
internal sealed record SchemaChange(SchemaChangeKind Kind, string Family, string Type, string? Field = null, string? FormerField = null, IReadOnlyList<string>? Properties = null)
{
    // The properties of a field whose change moves or retypes its stored value: a column of another type, a list where a
    // value was, or a column of the other table of a localized type.
    private static readonly string[] StorageProperties = ["type", "multiple", "localizable"];

    // A change that loses stored values when a database follows it: a type or a field removed, and a field whose kind changed.
    public bool IsDestructive => Kind switch
    {
        SchemaChangeKind.TypeRemoved or SchemaChangeKind.FieldRemoved => true,
        SchemaChangeKind.FieldChanged => Properties?.Any(StorageProperties.Contains) == true,
        _ => false,
    };

    internal string Label => Family == SchemaSnapshotDiff.ContentFamily ? Type : $"{Family} {Type}";

    public override string ToString() => Kind switch
    {
        SchemaChangeKind.TypeAdded => $"{Family} '{Type}' added",
        SchemaChangeKind.TypeRemoved => $"{Family} '{Type}' removed",
        SchemaChangeKind.TypeChanged => $"{Family} '{Type}' changed ({Join(Properties)})",
        SchemaChangeKind.FieldAdded => $"field '{Label}.{Field}' added",
        SchemaChangeKind.FieldRemoved => $"field '{Label}.{Field}' removed",
        SchemaChangeKind.FieldRenamed => $"field '{Label}.{FormerField}' renamed to '{Field}'",
        SchemaChangeKind.FieldChanged => $"field '{Label}.{Field}' changed ({Join(Properties)})",
        _ => $"fields of {Family} '{Type}' reordered",
    };

    private static string Join(IReadOnlyList<string>? properties) => string.Join(", ", properties ?? []);
}

// A field removed beside a field added of the same kind in one type, which may be one field renamed. Only [FormerNames] on
// the new field makes it a rename; the tools suggest the attribute and never apply it.
internal sealed record PossibleRename(string Family, string Type, string From, string To)
{
    public string Attribute => $"[FormerNames(\"{From}\")]";

    public override string ToString() =>
        $"possible rename: field '{(Family == SchemaSnapshotDiff.ContentFamily ? Type : $"{Family} {Type}")}.{From}' to '{To}'; add {Attribute} to '{To}' to make it one";
}
