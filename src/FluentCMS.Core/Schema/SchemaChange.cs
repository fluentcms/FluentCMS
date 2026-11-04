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
    private string Label => Family == SchemaSnapshotDiff.ContentFamily ? Type : $"{Family} {Type}";

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
