using FluentCMS.Core.Results;

namespace FluentCMS.Core.Entities;

// The facts of entities and their flags (ENT-02), read from the fact index written with every entity version, never from the
// documents (ADR-0005). Deleted entities are left out.
public interface IEntityFacts
{
    // The facts of the entity's latest draft, or of the version named.
    Task<Result<EntityFactSheet>> GetAsync(Guid entityId, int? version = null, CancellationToken cancellationToken = default);

    // The unsourced facts that the latest drafts and published versions hold, of every entity type or the one named by its slug.
    Task<Result<IReadOnlyList<FactFlag>>> UnsourcedAsync(string? contentType = null, CancellationToken cancellationToken = default);

    // The unsourced facts in the versions a change set's entity operations propose, for routing conditions (RTE-01).
    Task<Result<IReadOnlyList<FactFlag>>> UnsourcedInChangeSetAsync(Guid changeSetId, CancellationToken cancellationToken = default);
}

public sealed record EntityFactSheet(Guid EntityId, string Type, int Version, IReadOnlyList<Fact> Facts);

// A flagged fact of one version of an entity; IsLatest and IsPublished say which of the entity's pointers hold that version now.
public sealed record FactFlag(Guid EntityId, string Type, string Field, int Version, bool IsLatest, bool IsPublished);

public static class EntityErrorCodes
{
    public const string TypeNotEntity = "type_not_entity";
    public const string FieldNotRelation = "field_not_relation";
}
