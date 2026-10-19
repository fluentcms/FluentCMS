namespace FluentCMS.Core.Routing;

// Explicit routing rules applied to a set (RTE-01, D09). The order of the rules never matters: the strictest disposition that applies
// wins, and a set auto-publishes only when auto-publish rules cover every one of its items and every field each item changes, so a
// routine rule can never let a risky change through.
public static class RoutingEvaluator
{
    public static RoutingDecision Evaluate(IEnumerable<RoutingRule> rules, RoutingFacts facts)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(facts);

        var matched = new List<MatchedRoutingRule>();
        var covered = new HashSet<Guid>();

        // The fields of each item that auto-publish rules name, or null once a rule without a field condition covers the whole item.
        var autoPublished = new Dictionary<Guid, HashSet<string>?>();

        foreach (var rule in rules.OrderBy(rule => rule.Name, StringComparer.Ordinal))
        {
            var when = rule.Definition.When;
            if (!when.MatchesSet(facts))
            {
                continue;
            }

            var matches = facts.Items.Where(when.MatchesItem).ToList();
            if (matches.Count == 0)
            {
                continue;
            }

            var then = rule.Definition.Then;
            foreach (var item in matches)
            {
                if (then.Kind != DispositionKind.AutoPublish)
                {
                    covered.Add(item.ItemId);
                }
                else if (when.Fields is null)
                {
                    autoPublished[item.ItemId] = null;
                }
                else if (!autoPublished.TryGetValue(item.ItemId, out var fields) || fields is not null)
                {
                    autoPublished[item.ItemId] = [.. fields ?? [], .. when.Fields.Where(item.ChangedFields.Contains)];
                }
            }

            var items = matches.Select(item => item.ItemId).ToList();
            var count = items.Count == 1 ? "1 item" : $"{items.Count} items";
            matched.Add(new MatchedRoutingRule(
                rule.Name,
                rule.Version,
                then,
                items,
                $"The routing rule '{rule.Name}' (version {rule.Version}) applies to {count} and asks for {then}: {rule.Definition.Description}"));
        }

        covered.UnionWith(facts.Items
            .Where(item => autoPublished.TryGetValue(item.ItemId, out var fields) && (fields is null || item.ChangedFields.IsSubsetOf(fields)))
            .Select(item => item.ItemId));

        var unmatched = facts.Items.Select(item => item.ItemId).Where(id => !covered.Contains(id)).ToList();
        var strictest = matched
            .Select(rule => rule.Disposition)
            .Where(disposition => disposition.Kind != DispositionKind.AutoPublish)
            .Aggregate((RoutingDisposition?)null, (current, next) => current is null || next.IsStricterThan(current) ? next : current);

        var disposition = strictest ?? (matched.Count > 0 && unmatched.Count == 0 ? RoutingDisposition.AutoPublish : null);
        return new RoutingDecision(disposition, matched, unmatched);
    }
}
