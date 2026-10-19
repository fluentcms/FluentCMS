using FluentCMS.Core.Results;

namespace FluentCMS.Core.Entities;

// Relations between entities (ENT-01, D08): reference fields of entity types that point at other entities. A single reference
// relates an entity to one other, a list of references to many, so one-to-many and many-to-many are both the field's shape.
// Answers come from the reference index, never from the stored documents (ADR-0005).
public interface IEntityRelations
{
    Task<Result<EntityRelations>> GetAsync(Guid entityId, CancellationToken cancellationToken = default);
}

// What an entity's fields relate it to, and what relates to it.
public sealed record EntityRelations(Guid EntityId, string Type, IReadOnlyList<EntityRelation> Outgoing, IReadOnlyList<EntityRelation> Incoming);

// The field of the From entity that references the To entity, or one of its fields when ToField is set, such as product.price. A
// relation points at an entity, not a version, so it survives new versions of To; it belongs to the versions of From that hold it.
public sealed record EntityRelation(
    Guid FromId,
    string FromType,
    string Field,
    Guid ToId,
    string ToType,
    string? ToField,
    bool IsLatest,
    bool IsPublished);
