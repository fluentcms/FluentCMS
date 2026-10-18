using System.Text.Json;

namespace FluentCMS.Core.Content;

// One version of an entry as a reader sees it (CNT-04): each reference to an entity field holds that entity's current published
// value, or null where it could not be resolved, which Unresolved explains. References to whole entries and to media stay ids.
public sealed record ResolvedEntry(Guid EntryId, string ContentType, int Version, JsonElement Fields, IReadOnlyList<UnresolvedReference> Unresolved);

// Path is the JSON Pointer of the reference in the entry's fields, such as /price.
public sealed record UnresolvedReference(string Path, Guid EntityId, string Field, UnresolvedReason Reason);

public enum UnresolvedReason
{
    // The entity has no published version yet.
    NotPublished,

    // The entity's published version holds no value for the field.
    NoValue,

    // The entity is deleted, though it can still be restored.
    Deleted,
}
