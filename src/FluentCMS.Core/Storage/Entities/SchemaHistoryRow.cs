namespace FluentCMS.Core.Storage.Entities;

// A row of cms_schema_history: the schema the content tables were last brought up to, written when content migrations are
// applied. The newest row is what startup compares the code with.
public sealed class SchemaHistoryRow
{
    public Guid Id { get; set; }

    // SHA-256 of Snapshot as lowercase hex. Not unique: going back to an earlier schema adds a row with an earlier hash.
    public string Hash { get; set; } = "";

    // The canonical JSON of the schema, as text so that it keeps its bytes and hashes to Hash on every provider.
    public string Snapshot { get; set; } = "";

    public DateTimeOffset AppliedAt { get; set; }

    // The FluentCMS version that applied it, without build metadata.
    public string FluentcmsVersion { get; set; } = "";
}
