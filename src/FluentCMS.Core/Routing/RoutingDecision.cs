namespace FluentCMS.Core.Routing;

// A rule that applied to a set, at the version that routed it, with the items it matched.
public sealed record MatchedRoutingRule(string Name, int Version, RoutingDisposition Disposition, IReadOnlyList<Guid> Items, string Reason);

// Where explicit rules send a set (RTE-01, RTE-02). Disposition is the strictest of the rules that applied, or auto-publish when
// auto-publish rules cover every change of every item and nothing stricter applies; null when no rule settled it. UnmatchedItems
// are the items with a change no rule decides, which the risk score routes.
public sealed record RoutingDecision(
    RoutingDisposition? Disposition,
    IReadOnlyList<MatchedRoutingRule> Rules,
    IReadOnlyList<Guid> UnmatchedItems)
{
    public IReadOnlyList<string> Reasons { get; } = BuildReasons(Rules, UnmatchedItems);

    private static List<string> BuildReasons(IReadOnlyList<MatchedRoutingRule> rules, IReadOnlyList<Guid> unmatched)
    {
        var reasons = rules.Select(rule => rule.Reason).ToList();
        if (unmatched.Count > 0)
        {
            reasons.Add(unmatched.Count == 1
                ? "1 item has a change no routing rule decides, so the set needs a risk score."
                : $"{unmatched.Count} items have changes no routing rule decides, so the set needs a risk score.");
        }

        return reasons;
    }
}
