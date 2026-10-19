namespace FluentCMS.Core.Routing;

// Explicit routing rules applied to a set (RTE-01, D09). The order of the rules never matters: the strictest disposition that applies
// wins, and a set auto-publishes only when every one of its items is matched by an auto-publish rule, so a routine rule can never
// let a risky change through.
public static class RoutingEvaluator
{
    public static RoutingDecision Evaluate(IEnumerable<RoutingRule> rules, RoutingFacts facts)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(facts);

        var matched = new List<MatchedRoutingRule>();
        var covered = new HashSet<Guid>();

        foreach (var rule in rules.OrderBy(rule => rule.Name, StringComparer.Ordinal))
        {
            var when = rule.Definition.When;
            if (!when.MatchesSet(facts))
            {
                continue;
            }

            var items = facts.Items.Where(when.MatchesItem).Select(item => item.ItemId).ToList();
            if (items.Count == 0)
            {
                continue;
            }

            covered.UnionWith(items);
            var then = rule.Definition.Then;
            var count = items.Count == 1 ? "1 item" : $"{items.Count} items";
            matched.Add(new MatchedRoutingRule(
                rule.Name,
                rule.Version,
                then,
                items,
                $"The routing rule '{rule.Name}' (version {rule.Version}) applies to {count} and asks for {then}: {rule.Definition.Description}"));
        }

        var unmatched = facts.Items.Select(item => item.ItemId).Where(id => !covered.Contains(id)).ToList();
        var strictest = matched
            .Select(rule => rule.Disposition)
            .Where(disposition => disposition.Kind != DispositionKind.AutoPublish)
            .Aggregate((RoutingDisposition?)null, (current, next) => current is null || next.IsStricterThan(current) ? next : current);

        var disposition = strictest ?? (matched.Count > 0 && unmatched.Count == 0 ? RoutingDisposition.AutoPublish : null);
        return new RoutingDecision(disposition, matched, unmatched);
    }
}
