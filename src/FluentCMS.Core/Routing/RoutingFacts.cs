using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Identity;

namespace FluentCMS.Core.Routing;

// The worst outcome of a set's checks (CHK-03), as routing conditions name it.
public enum RoutingCheckOutcome
{
    Pass,
    Warn,
    Fail,
}

// One item of a set as routing sees it. ContentType is the type's slug for a schema item, the entry's type for an entry or an
// entity, and null for kinds without one. ChangedFields are the names of the fields the diff lists for the item.
public sealed record RoutingItem(ItemKind ItemKind, Guid ItemId, OperationKind ChangeKind, string? ContentType, IReadOnlySet<string> ChangedFields);

// What routing knows about a set. Checks and RiskScore stay null until the check run (F-CHK) and the scorer (RTE-02) supply them,
// and a condition on either does not match without them.
public sealed record RoutingFacts(IReadOnlyList<RoutingItem> Items, Initiator Initiator, RoutingCheckOutcome? Checks = null, int? RiskScore = null);
