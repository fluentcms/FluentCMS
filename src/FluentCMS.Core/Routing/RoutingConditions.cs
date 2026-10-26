using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Identity;

namespace FluentCMS.Core.Routing;

public enum InitiatorCondition
{
    Person,
    Agent,
}

// The `when` of a routing rule (RTE-01). Item conditions pick the items a rule applies to, set conditions decide whether it applies
// to the set at all. A condition left null matches anything; the values of one condition are alternatives.
public sealed record RoutingConditions
{
    public const int HighestRiskScore = 100;

    public static RoutingConditions Any { get; } = new();

    public IReadOnlyList<ItemKind>? ItemKinds { get; init; }

    public IReadOnlyList<string>? ContentTypes { get; init; }

    // Matches an item that changes at least one of these fields. An auto-publish rule covers only the fields it names, so any other
    // field the item changes, or a change to the item as a whole, still needs a rule of its own (RoutingEvaluator).
    public IReadOnlyList<string>? Fields { get; init; }

    public IReadOnlyList<OperationKind>? ChangeKinds { get; init; }

    public IReadOnlyList<InitiatorCondition>? Initiators { get; init; }

    // Agent credential ids.
    public IReadOnlyList<Guid>? Agents { get; init; }

    // The worst check outcome the set may have: pass allows only passing checks, warn allows warnings too.
    public RoutingCheckOutcome? Checks { get; init; }

    public int? MinRiskScore { get; init; }

    public int? MaxRiskScore { get; init; }

    public bool MatchesSet(RoutingFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);

        if (Initiators is not null && !Initiators.Contains(facts.Initiator is AgentInitiator ? InitiatorCondition.Agent : InitiatorCondition.Person))
        {
            return false;
        }

        if (Agents is not null && (facts.Initiator is not AgentInitiator agent || !Agents.Contains(agent.CredentialId)))
        {
            return false;
        }

        if (Checks is { } allowed && (facts.Checks is not { } outcome || outcome > allowed))
        {
            return false;
        }

        if (MinRiskScore is not null || MaxRiskScore is not null)
        {
            return facts.RiskScore is { } score && score >= (MinRiskScore ?? 0) && score <= (MaxRiskScore ?? HighestRiskScore);
        }

        return true;
    }

    public bool MatchesItem(RoutingItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        return (ItemKinds is null || ItemKinds.Contains(item.ItemKind))
            && (ContentTypes is null || (item.ContentType is { } slug && ContentTypes.Contains(slug, StringComparer.Ordinal)))
            && (Fields is null || Fields.Any(item.ChangedFields.Contains))
            && (ChangeKinds is null || ChangeKinds.Contains(item.ChangeKind));
    }
}
