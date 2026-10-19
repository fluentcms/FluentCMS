using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Routing;

namespace FluentCMS.Tests.Routing;

public sealed class RoutingEvaluatorTests
{
    private static readonly AgentInitiator Agent = new(Guid.NewGuid(), "catalog-agent");
    private static readonly UserInitiator Person = new(Guid.NewGuid(), "Amir");

    // The S1 rules: a product's price needs one approval, a spec's name is routine.
    private static readonly RoutingRule PriceChanges = Rule(
        "price-changes",
        new RoutingConditions { ContentTypes = ["product"], Fields = ["price"] },
        RoutingDisposition.Review(1));

    private static readonly RoutingRule SpecNames = Rule(
        "spec-names",
        new RoutingConditions { ContentTypes = ["spec"], Fields = ["name"], ChangeKinds = [OperationKind.Update] },
        RoutingDisposition.AutoPublish);

    [Fact]
    public void A_price_change_needs_one_approval()
    {
        var price = Item("product", OperationKind.Update, "price");

        var decision = RoutingEvaluator.Evaluate([PriceChanges, SpecNames], Facts(price));

        Assert.Equal(RoutingDisposition.Review(1), decision.Disposition);
        var rule = Assert.Single(decision.Rules);
        Assert.Equal(("price-changes", 3), (rule.Name, rule.Version));
        Assert.Equal([price.ItemId], rule.Items);
        Assert.Contains("'price-changes' (version 3) applies to 1 item and asks for review with 1 approval: Routes the set.", rule.Reason);
        Assert.Empty(decision.UnmatchedItems);
    }

    [Fact]
    public void A_set_of_only_routine_changes_auto_publishes()
    {
        var decision = RoutingEvaluator.Evaluate([PriceChanges, SpecNames], Facts(Item("spec", OperationKind.Update, "name"), Item("spec", OperationKind.Update, "name")));

        Assert.Equal(RoutingDisposition.AutoPublish, decision.Disposition);
        Assert.Equal(["spec-names"], decision.Rules.Select(rule => rule.Name));
    }

    [Fact]
    public void A_routine_rule_never_lets_a_risky_change_through_whatever_the_order()
    {
        var facts = Facts(Item("spec", OperationKind.Update, "name"), Item("product", OperationKind.Update, "price"));

        var first = RoutingEvaluator.Evaluate([SpecNames, PriceChanges], facts);
        var second = RoutingEvaluator.Evaluate([PriceChanges, SpecNames], facts);

        Assert.Equal(RoutingDisposition.Review(1), first.Disposition);
        Assert.Equal(RoutingDisposition.Review(1), second.Disposition);
        Assert.Equal(2, first.Rules.Count);
    }

    // An auto-publish rule covers the fields it names, not the whole item, so a change to another field of the same item still waits.
    [Fact]
    public void An_auto_publish_rule_covers_only_the_fields_it_names()
    {
        var item = Item("spec", OperationKind.Update, "name", "unit");
        var units = Rule("spec-units", new RoutingConditions { ContentTypes = ["spec"], Fields = ["unit"] }, RoutingDisposition.AutoPublish);
        var whole = Rule("specs", new RoutingConditions { ContentTypes = ["spec"] }, RoutingDisposition.AutoPublish);

        var nameOnly = RoutingEvaluator.Evaluate([SpecNames], Facts(item));

        Assert.Null(nameOnly.Disposition);
        Assert.Equal([item.ItemId], nameOnly.UnmatchedItems);
        Assert.Equal(["spec-names"], nameOnly.Rules.Select(rule => rule.Name));
        Assert.Equal(RoutingDisposition.AutoPublish, RoutingEvaluator.Evaluate([SpecNames, units], Facts(item)).Disposition);
        Assert.Equal(RoutingDisposition.AutoPublish, RoutingEvaluator.Evaluate([SpecNames, whole], Facts(item)).Disposition);
    }

    // A change to the item as a whole, such as a schema becoming an entity type, is no field, so only a rule for the whole item covers it.
    [Fact]
    public void A_change_to_the_item_as_a_whole_needs_a_rule_without_a_field_condition()
    {
        var item = Item("spec", OperationKind.Update, "name") with { ChangedProperties = new HashSet<string>(["entity"]) };
        var whole = Rule("specs", new RoutingConditions { ContentTypes = ["spec"] }, RoutingDisposition.AutoPublish);

        var decision = RoutingEvaluator.Evaluate([SpecNames], Facts(item));

        Assert.Null(decision.Disposition);
        Assert.Equal([item.ItemId], decision.UnmatchedItems);
        Assert.Contains("1 item has a change no routing rule decides, so the set needs a risk score.", decision.Reasons);
        Assert.Equal(RoutingDisposition.AutoPublish, RoutingEvaluator.Evaluate([SpecNames, whole], Facts(item)).Disposition);
    }

    // A create or a delete lists every field it adds or removes, but it changes the item as a whole, so naming those fields is not enough.
    [Fact]
    public void A_create_or_delete_needs_a_rule_without_a_field_condition()
    {
        var titles = Rule("category-titles", new RoutingConditions { ContentTypes = ["category"], Fields = ["title"] }, RoutingDisposition.AutoPublish);
        var categories = Rule("categories", new RoutingConditions { ContentTypes = ["category"] }, RoutingDisposition.AutoPublish);
        var created = Item("category", OperationKind.Create, "title");
        var deleted = Item("category", OperationKind.Delete, "title");

        Assert.Equal([created.ItemId, deleted.ItemId], RoutingEvaluator.Evaluate([titles], Facts(created, deleted)).UnmatchedItems);
        Assert.Equal(RoutingDisposition.AutoPublish, RoutingEvaluator.Evaluate([titles, categories], Facts(created, deleted)).Disposition);
        Assert.Equal(RoutingDisposition.AutoPublish, RoutingEvaluator.Evaluate([titles], Facts(Item("category", OperationKind.Update, "title"))).Disposition);
    }

    [Fact]
    public void Block_beats_review_and_review_takes_the_highest_count()
    {
        var item = Item("product", OperationKind.Update, "price");
        var two = Rule("two", new RoutingConditions { ContentTypes = ["product"] }, RoutingDisposition.Review(2));
        var block = Rule("block", new RoutingConditions { Fields = ["price"] }, RoutingDisposition.Block);

        Assert.Equal(RoutingDisposition.Review(2), RoutingEvaluator.Evaluate([PriceChanges, two], Facts(item)).Disposition);
        Assert.Equal(RoutingDisposition.Block, RoutingEvaluator.Evaluate([PriceChanges, two, block], Facts(item)).Disposition);
        Assert.True(RoutingDisposition.Block.IsStricterThan(RoutingDisposition.Review(RoutingDisposition.MaxApprovals)));
        Assert.True(RoutingDisposition.Review(1).IsStricterThan(RoutingDisposition.AutoPublish));
    }

    [Fact]
    public void An_item_no_rule_matches_is_listed_and_stops_auto_publish()
    {
        var name = Item("spec", OperationKind.Update, "name");
        var unit = Item("spec", OperationKind.Update, "unit");
        var created = Item("spec", OperationKind.Create, "name");

        var decision = RoutingEvaluator.Evaluate([SpecNames], Facts(name, unit, created));

        Assert.Null(decision.Disposition);
        Assert.Equal([unit.ItemId, created.ItemId], decision.UnmatchedItems);
        Assert.Contains("2 items have changes no routing rule decides, so the set needs a risk score.", decision.Reasons);
        Assert.Null(RoutingEvaluator.Evaluate([], Facts(name)).Disposition);
    }

    [Fact]
    public void A_stricter_rule_decides_even_while_some_items_wait_for_a_score()
    {
        var price = Item("product", OperationKind.Update, "price");
        var other = Item("region", OperationKind.Update, "code");

        var decision = RoutingEvaluator.Evaluate([PriceChanges], Facts(price, other));

        Assert.Equal(RoutingDisposition.Review(1), decision.Disposition);
        Assert.Equal([other.ItemId], decision.UnmatchedItems);
    }

    [Fact]
    public void Item_conditions_match_by_kind_type_field_and_change()
    {
        var conditions = new RoutingConditions
        {
            ItemKinds = [ItemKind.Entity],
            ContentTypes = ["product"],
            Fields = ["price", "currency"],
            ChangeKinds = [OperationKind.Update],
        };

        Assert.True(conditions.MatchesItem(Item("product", OperationKind.Update, "currency", "name")));
        Assert.False(conditions.MatchesItem(Item("product", OperationKind.Update, "name")));
        Assert.False(conditions.MatchesItem(Item("product", OperationKind.Create, "price")));
        Assert.False(conditions.MatchesItem(Item("spec", OperationKind.Update, "price")));
        Assert.False(conditions.MatchesItem(Item("product", OperationKind.Update, "price") with { ItemKind = ItemKind.Entry }));
        Assert.False(conditions.MatchesItem(Item("product", OperationKind.Update, "price") with { ContentType = null }));
    }

    [Fact]
    public void Set_conditions_match_by_initiator_and_agent_and_need_facts_for_checks_and_scores()
    {
        var item = Item("product", OperationKind.Update, "price");
        var byAgents = new RoutingConditions { Initiators = [InitiatorCondition.Agent] };
        var byThisAgent = new RoutingConditions { Agents = [Agent.CredentialId] };
        var passing = new RoutingConditions { Checks = RoutingCheckOutcome.Warn };
        var lowRisk = new RoutingConditions { MaxRiskScore = 30 };

        Assert.True(byAgents.MatchesSet(Facts(item)));
        Assert.False(byAgents.MatchesSet(Facts(item) with { Initiator = Person }));
        Assert.True(byThisAgent.MatchesSet(Facts(item)));
        Assert.False(byThisAgent.MatchesSet(Facts(item) with { Initiator = new AgentInitiator(Guid.NewGuid(), "other") }));

        Assert.False(passing.MatchesSet(Facts(item)));
        Assert.True(passing.MatchesSet(Facts(item) with { Checks = RoutingCheckOutcome.Pass }));
        Assert.True(passing.MatchesSet(Facts(item) with { Checks = RoutingCheckOutcome.Warn }));
        Assert.False(passing.MatchesSet(Facts(item) with { Checks = RoutingCheckOutcome.Fail }));

        Assert.False(lowRisk.MatchesSet(Facts(item)));
        Assert.True(lowRisk.MatchesSet(Facts(item) with { RiskScore = 30 }));
        Assert.False(lowRisk.MatchesSet(Facts(item) with { RiskScore = 31 }));
    }

    private static RoutingRule Rule(string name, RoutingConditions when, RoutingDisposition then)
    {
        return RoutingRule.Restore(Guid.NewGuid(), Guid.NewGuid(), name, 3, 3, 3, new RoutingRuleDefinition("Routes the set.", when, then));
    }

    private static RoutingItem Item(string contentType, OperationKind change, params string[] fields)
    {
        return new RoutingItem(ItemKind.Entity, Guid.NewGuid(), change, contentType, fields.ToHashSet(StringComparer.Ordinal));
    }

    private static RoutingFacts Facts(params RoutingItem[] items)
    {
        return new RoutingFacts(items, Agent);
    }
}
