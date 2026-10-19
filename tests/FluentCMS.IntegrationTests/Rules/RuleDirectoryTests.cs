using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Results;
using FluentCMS.Core.Rules;
using FluentCMS.Infrastructure.Persistence;
using FluentCMS.IntegrationTests.ChangeSets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FluentCMS.IntegrationTests.ChangeSets.ChangeSetScenario;

namespace FluentCMS.IntegrationTests.Rules;

// RUL-01, RUL-03: rules are versioned content, written into change sets and active only once a set publishes them.
public sealed class RuleDirectoryTests
{
    private static readonly RuleDefinition NoSuperlatives = new(
        RuleKind.VoiceAndStyle,
        "Never claim best in class.",
        CheckSeverity.Block,
        new RuleScope(["product-page"], ["headline"]));

    private static readonly RuleDefinition AvoidSuperlatives = new(
        RuleKind.VoiceAndStyle,
        "Avoid superlatives such as best in class.",
        CheckSeverity.Warn,
        new RuleScope(["product-page"]));

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_rule_is_a_draft_until_its_change_set_publishes_it(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var changeSet = await scenario.OpenAsync();

        var created = (await RulesAsync(scenario, rules => rules.CreateAsync(new NewRule("no-superlatives", NoSuperlatives), ChangeSetTarget.Existing(changeSet.Id)))).Value;

        Assert.Equal((1, RuleState.Draft, (int?)null), (created.Version, created.State, created.ActiveVersion));
        var operation = Assert.Single((await scenario.RunAsync(directory => directory.GetAsync(changeSet.Id))).Value.Operations);
        Assert.Equal((OperationKind.Create, ItemKind.Rule, created.Id, (int?)null, (int?)1), (operation.Kind, operation.ItemKind, operation.ItemId, operation.BaseVersion, operation.ProposedVersion));
        Assert.Equal(["change_set.opened", "rule.created", "change_set.operation_added"], (await scenario.ChangeSetEventsAsync(changeSet.Id)).Select(row => row.Operation));
        Assert.Equal(RuleErrorCodes.NotActive, Assert.Single((await RulesAsync(scenario, rules => rules.GetActiveAsync("no-superlatives"))).Errors).Code);

        await scenario.PublishAsync(changeSet.Id);

        var active = (await RulesAsync(scenario, rules => rules.GetActiveAsync("no-superlatives"))).Value;
        Assert.Equal((1, RuleState.Active, "Never claim best in class."), (active.Version, active.State, active.Definition.Text));
        Assert.Contains(await scenario.ChangeSetEventsAsync(changeSet.Id), row => row.Operation == "rule.published" && row.SubjectId == created.Id);
    }

    // A revision is the next version, and the active one stays until its set publishes; the diff shows what changes.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_revision_leaves_the_active_version_until_its_set_publishes_and_its_diff_shows_the_change(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        await CreatedAndPublishedAsync(scenario);
        var revising = await scenario.OpenAsync();

        var revised = (await RulesAsync(scenario, rules => rules.ReviseAsync("no-superlatives", 1, AvoidSuperlatives, ChangeSetTarget.Existing(revising.Id)))).Value;
        var again = (await RulesAsync(scenario, rules => rules.ReviseAsync("no-superlatives", 2, AvoidSuperlatives, ChangeSetTarget.Existing(revising.Id)))).Value;

        Assert.Equal((2, 3), (revised.Version, again.Version));
        var operation = Assert.Single((await scenario.RunAsync(directory => directory.GetAsync(revising.Id))).Value.Operations);
        Assert.Equal((OperationKind.Update, (int?)1, (int?)3), (operation.Kind, operation.BaseVersion, operation.ProposedVersion));
        Assert.Equal(1, (await RulesAsync(scenario, rules => rules.GetActiveAsync("no-superlatives"))).Value.Version);
        var listed = Assert.Single(await RulesAsync(scenario, rules => rules.ListAsync()));
        Assert.Equal((3, (int?)1), (listed.Version, listed.ActiveVersion));

        var diff = Assert.Single((await scenario.RunAsync(directory => directory.DiffAsync(revising.Id))).Value.Items);
        Assert.Equal(["scope.fields", "severity", "text"], diff.Properties.Select(property => property.Name).Order(StringComparer.Ordinal));
        var text = diff.Properties.Single(property => property.Name == "text");
        Assert.Equal(("Never claim best in class.", "Avoid superlatives such as best in class."), (text.Before!.Value.GetString(), text.After!.Value.GetString()));
        var fields = diff.Properties.Single(property => property.Name == "scope.fields");
        Assert.Equal(("""["headline"]""", (string?)null), (fields.Before!.Value.GetRawText(), fields.After?.GetRawText()));

        await scenario.PublishAsync(revising.Id);

        var published = (await RulesAsync(scenario, rules => rules.GetActiveAsync("no-superlatives"))).Value;
        Assert.Equal((3, CheckSeverity.Warn), (published.Version, published.Definition.Severity));
    }

    // RUL-03: retiring takes a rule out of the rule book once its set publishes, and reverting that set makes it active again.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_retired_rule_is_active_again_when_its_retirement_is_reverted(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        await CreatedAndPublishedAsync(scenario);
        var retiring = await scenario.OpenAsync();

        await RulesAsync(scenario, rules => rules.RetireAsync("no-superlatives", ChangeSetTarget.Existing(retiring.Id)));
        Assert.Equal(RuleState.Active, (await RulesAsync(scenario, rules => rules.GetAsync("no-superlatives"))).Value.State);
        await scenario.PublishAsync(retiring.Id);

        var retired = (await RulesAsync(scenario, rules => rules.GetAsync("no-superlatives"))).Value;
        Assert.Equal((RuleState.Retired, (int?)null), (retired.State, retired.ActiveVersion));
        var another = await scenario.OpenAsync();
        Assert.Equal(RuleErrorCodes.NotActive, Assert.Single((await RulesAsync(scenario, rules => rules.RetireAsync("no-superlatives", ChangeSetTarget.Existing(another.Id)))).Errors).Code);

        var revert = (await scenario.AsPersonAsync(directory => directory.RevertAsync(retiring.Id, "The rule still applies."))).Value;
        await scenario.PublishAsync(revert.Id);

        var restored = (await RulesAsync(scenario, rules => rules.GetActiveAsync("no-superlatives"))).Value;
        Assert.Equal((RuleState.Active, 1), (restored.State, restored.Version));
    }

    // A revert restores what its set changed: reverting a rule's creation makes it the draft it was, not a retired rule.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Reverting_a_rules_creation_makes_it_a_draft_again(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var changeSet = await scenario.OpenAsync();
        await RulesAsync(scenario, rules => rules.CreateAsync(new NewRule("no-superlatives", NoSuperlatives), ChangeSetTarget.Existing(changeSet.Id)));
        await scenario.PublishAsync(changeSet.Id);

        var revert = (await scenario.AsPersonAsync(directory => directory.RevertAsync(changeSet.Id, "Not yet."))).Value;
        await scenario.PublishAsync(revert.Id);

        var rule = (await RulesAsync(scenario, rules => rules.GetAsync("no-superlatives"))).Value;
        Assert.Equal((RuleState.Draft, (int?)null), (rule.State, rule.ActiveVersion));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_rule_that_cannot_be_held_or_whose_slug_is_taken_is_refused_and_nothing_is_stored(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        await CreatedAndPublishedAsync(scenario);
        var changeSet = await scenario.OpenAsync();
        var target = ChangeSetTarget.Existing(changeSet.Id);

        var invalid = await RulesAsync(scenario, rules => rules.CreateAsync(new NewRule("loud", new RuleDefinition((RuleKind)9, " ", (CheckSeverity)5)), target));
        var taken = await RulesAsync(scenario, rules => rules.CreateAsync(new NewRule("no-superlatives", NoSuperlatives), target));
        var unknown = await RulesAsync(scenario, rules => rules.ReviseAsync("no-such-rule", 1, NoSuperlatives, target));
        var stale = await RulesAsync(scenario, rules => rules.ReviseAsync("no-superlatives", 5, NoSuperlatives, target));

        Assert.Equal([RuleErrorCodes.KindUnknown, RuleErrorCodes.TextInvalid, RuleErrorCodes.SeverityUnknown], invalid.Errors.Select(error => error.Code));
        Assert.Equal(RuleErrorCodes.SlugTaken, Assert.Single(taken.Errors).Code);
        Assert.Equal(RuleErrorCodes.NotFound, Assert.Single(unknown.Errors).Code);
        Assert.Equal(RuleErrorCodes.VersionConflict, Assert.Single(stale.Errors).Code);
        Assert.Empty((await scenario.RunAsync(directory => directory.GetAsync(changeSet.Id))).Value.Operations);
        Assert.Equal(1, await scenario.QueryAsync(context => context.Rules.CountAsync()));
        Assert.Equal(1, await scenario.QueryAsync(context => context.RuleVersions.CountAsync()));
    }

    // M1: any administrator writes rules; an agent reads the rule book, and an engine writes no content.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_agent_and_an_engine_are_refused_every_rule_write(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var rule = await CreatedAndPublishedAsync(scenario);
        var changeSet = await scenario.OpenAsync();
        var target = ChangeSetTarget.Existing(changeSet.Id);
        var refusals = new List<Result<Rule>>();

        foreach (var initiator in new Initiator[] { Agent, SystemInitiator.Checks })
        {
            scenario.Initiator = initiator;
            refusals.Add(await RulesAsync(scenario, rules => rules.CreateAsync(new NewRule("more-rules", NoSuperlatives), target)));
            refusals.Add(await RulesAsync(scenario, rules => rules.ReviseAsync("no-superlatives", 1, AvoidSuperlatives, target)));
            refusals.Add(await RulesAsync(scenario, rules => rules.RetireAsync("no-superlatives", target)));
        }

        scenario.Initiator = Agent;
        var agentsSet = await scenario.OpenAsync();
        var added = await scenario.RunAsync(directory => directory.AddOperationAsync(
            agentsSet.Id,
            ChangeOperation.Create(OperationKind.Delete, ItemKind.Rule, rule.Id, 1, null).Value));

        Assert.All(refusals, refused => Assert.Equal(RuleErrorCodes.PeopleOnly, Assert.Single(refused.Errors).Code));
        Assert.Equal(RuleErrorCodes.PeopleOnly, Assert.Single(added.Errors).Code);
        Assert.Equal(1, await scenario.QueryAsync(context => context.RuleVersions.CountAsync()));
    }

    private static async Task<ChangeSetScenario> ScenarioAsync(string provider)
    {
        var scenario = await ChangeSetScenario.CreateAsync(provider);
        scenario.Initiator = Amir;
        return scenario;
    }

    private static async Task<Rule> CreatedAndPublishedAsync(ChangeSetScenario scenario)
    {
        var changeSet = await scenario.OpenAsync();
        var created = (await RulesAsync(scenario, rules => rules.CreateAsync(new NewRule("no-superlatives", NoSuperlatives), ChangeSetTarget.Existing(changeSet.Id)))).Value;
        await scenario.PublishAsync(changeSet.Id);
        return created;
    }

    // The rule directory as whoever Initiator names, in a scope of its own.
    private static async Task<T> RulesAsync<T>(ChangeSetScenario scenario, Func<IRuleDirectory, Task<T>> action)
    {
        await using var scope = scenario.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IRuleDirectory>());
    }
}
