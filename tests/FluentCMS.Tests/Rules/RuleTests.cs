using FluentCMS.Core.Checks;
using FluentCMS.Core.Rules;
using FluentCMS.Core.Sites;

namespace FluentCMS.Tests.Rules;

public sealed class RuleTests
{
    private static readonly RuleDefinition NoSuperlatives = new(
        RuleKind.VoiceAndStyle,
        "Never claim best in class.",
        CheckSeverity.Block,
        new RuleScope(["product-page"], ["headline", "body"], ["en"]));

    // RUL-02, RUL-03: a rule has a kind, prose, a severity and a scope of types, fields and locales, stored as one document.
    [Fact]
    public void A_definition_round_trips_through_its_stored_document()
    {
        var json = NoSuperlatives.ToJson();
        var read = RuleDefinition.Parse(json).Value;

        Assert.Equal(
            """{"kind":"voice_and_style","text":"Never claim best in class.","severity":"block","scope":{"contentTypes":["product-page"],"fields":["headline","body"],"locales":["en"]}}""",
            json);
        Assert.Equal((RuleKind.VoiceAndStyle, "Never claim best in class.", CheckSeverity.Block), (read.Kind, read.Text, read.Severity));
        Assert.Equal(["product-page"], read.Scope.ContentTypes!);
        Assert.Equal(["headline", "body"], read.Scope.Fields!);
        Assert.Equal(["en"], read.Scope.Locales!);
        Assert.Equal("""{"kind":"factual","text":"Prices come from the catalog.","severity":"warn"}""", new RuleDefinition(RuleKind.Factual, "Prices come from the catalog.", CheckSeverity.Warn).ToJson());
    }

    [Theory]
    [InlineData(RuleKind.VoiceAndStyle, "voice_and_style")]
    [InlineData(RuleKind.Factual, "factual")]
    [InlineData(RuleKind.Structural, "structural")]
    [InlineData(RuleKind.PermissionAndRouting, "permission_and_routing")]
    public void Each_kind_has_a_stored_name(RuleKind kind, string name)
    {
        Assert.Equal((name, (RuleKind?)kind), (RuleDefinition.KindName(kind), RuleDefinition.KindOf(name)));
    }

    // A new rule is a draft at version 1, active only once a change set publishes it.
    [Fact]
    public void A_new_rule_is_a_draft_at_version_one()
    {
        var rule = Rule.Create(Site.Default.Id, new NewRule("no-superlatives", NoSuperlatives)).Value;

        Assert.Equal((1, RuleState.Draft, (int?)null), (rule.Version, rule.State, rule.ActiveVersion));
    }

    [Fact]
    public void A_rule_that_cannot_be_held_is_refused_with_what_to_change()
    {
        var definition = new RuleDefinition((RuleKind)42, " ", (CheckSeverity)7, new RuleScope([], ["Head Line"], ["English"]));

        var created = Rule.Create(Site.Default.Id, new NewRule("No Superlatives", definition));

        Assert.Equal(
            [RuleErrorCodes.SlugInvalid, RuleErrorCodes.KindUnknown, RuleErrorCodes.TextInvalid, RuleErrorCodes.SeverityUnknown, RuleErrorCodes.ScopeInvalid, RuleErrorCodes.ScopeInvalid, RuleErrorCodes.ScopeInvalid],
            created.Errors.Select(error => error.Code));
        Assert.Contains("'Head Line'", created.Errors[5].Message);
    }

    // A revision builds on the latest version or the active one, never on one its author has not seen.
    [Fact]
    public void A_revision_makes_the_next_version_from_the_one_its_author_read()
    {
        var active = Rule.Restore(Guid.NewGuid(), Site.Default.Id, "no-superlatives", 2, NoSuperlatives, 1, RuleState.Active);
        var warning = new RuleDefinition(RuleKind.VoiceAndStyle, "Avoid superlatives.", CheckSeverity.Warn);

        var fromLatest = active.Revise(2, warning).Value;
        var fromActive = active.Revise(1, warning).Value;
        var stale = active.Revise(3, warning);

        Assert.Equal((3, (int?)1, RuleState.Active, "Avoid superlatives."), (fromLatest.Version, fromLatest.ActiveVersion, fromLatest.State, fromLatest.Definition.Text));
        Assert.Equal(3, fromActive.Version);
        Assert.Equal(RuleErrorCodes.VersionConflict, Assert.Single(stale.Errors).Code);
    }

    [Theory]
    [InlineData("""{"kind":"voice_and_style","text":"x"}""")]
    [InlineData("""{"kind":"style","text":"x","severity":"block"}""")]
    [InlineData("""{"kind":"factual","text":"x","severity":"Warn"}""")]
    [InlineData("""{"kind":"factual","text":"x","severity":"block","scope":{"fields":"body"}}""")]
    [InlineData("""{"kind":"factual","kind":"factual","text":"x","severity":"block"}""")]
    public void A_stored_document_that_is_not_a_rule_says_why(string json)
    {
        Assert.Equal(RuleErrorCodes.DefinitionMalformed, Assert.Single(RuleDefinition.Parse(json).Errors).Code);
    }
}
