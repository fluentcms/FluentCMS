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

// One difference between two snapshots. Properties are the JSON keys that differ, without their values, which a log
// must not carry (a default value can be anything).
internal sealed record SchemaChange(SchemaChangeKind Kind, string Type, string? Field = null, string? FormerField = null, IReadOnlyList<string>? Properties = null)
{
    public override string ToString() => Kind switch
    {
        SchemaChangeKind.TypeAdded => $"type '{Type}' added",
        SchemaChangeKind.TypeRemoved => $"type '{Type}' removed",
        SchemaChangeKind.TypeChanged => $"type '{Type}' changed ({Join(Properties)})",
        SchemaChangeKind.FieldAdded => $"field '{Type}.{Field}' added",
        SchemaChangeKind.FieldRemoved => $"field '{Type}.{Field}' removed",
        SchemaChangeKind.FieldRenamed => $"field '{Type}.{FormerField}' renamed to '{Field}'",
        SchemaChangeKind.FieldChanged => $"field '{Type}.{Field}' changed ({Join(Properties)})",
        _ => $"fields of type '{Type}' reordered",
    };

    private static string Join(IReadOnlyList<string>? properties) => string.Join(", ", properties ?? []);
}
