namespace FluentCMS.Schema;

/// <summary>
/// A <see cref="SchemaModel"/> as plain data, for clients such as the admin, type generators and agents. It is what
/// <see cref="ISchemaIntrospection"/> returns and what the Management API serves at <c>/schema</c>, so a client can read
/// it back into these records.
/// </summary>
public sealed record SchemaDescription
{
    /// <summary>The collections and singletons, ordered by name.</summary>
    public required IReadOnlyList<SchemaTypeDescription> Types { get; init; }

    /// <summary>The components, ordered by name.</summary>
    public required IReadOnlyList<SchemaTypeDescription> Components { get; init; }

    /// <summary>The blocks, ordered by name. A blocks field names the ones it allows in its <c>blocks</c> setting.</summary>
    public required IReadOnlyList<SchemaTypeDescription> Blocks { get; init; }
}
