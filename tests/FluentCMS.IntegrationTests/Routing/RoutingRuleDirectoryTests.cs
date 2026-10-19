using System.Text.Json;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Content;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Routing;
using FluentCMS.Infrastructure.Persistence;
using FluentCMS.IntegrationTests.ChangeSets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FluentCMS.IntegrationTests.ChangeSets.ChangeSetScenario;

namespace FluentCMS.IntegrationTests.Routing;

public sealed class RoutingRuleDirectoryTests
{
    private static readonly RoutingRuleDefinition SummaryChanges = Definition(
        "A changed product summary needs a person.",
        new RoutingConditions { ContentTypes = ["product"], Fields = ["summary"] },
        RoutingDisposition.Review(1));

    private static readonly RoutingRuleDefinition NewCategories = Definition(
        "New categories from people are routine.",
        new RoutingConditions { ContentTypes = ["category"], ChangeKinds = [OperationKind.Create], Initiators = [InitiatorCondition.Person] },
        RoutingDisposition.AutoPublish);

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_person_creates_revises_activates_and_deactivates_a_rule_and_each_write_is_audited(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);

        var created = (await RunAsync(scenario, rules => rules.CreateAsync(new NewRoutingRule("summary-changes", SummaryChanges)))).Value;
        Assert.Equal((1, 1, (int?)null), (created.Version, created.LatestVersion, created.ActiveVersion));
        Assert.Empty(await RunAsync(scenario, rules => rules.ListActiveAsync()));

        await RunAsync(scenario, rules => rules.ActivateAsync("summary-changes", 1));
        var revised = (await RunAsync(scenario, rules => rules.ReviseAsync("summary-changes", 1, Definition("Two people.", SummaryChanges.When, RoutingDisposition.Review(2))))).Value;
        Assert.Equal((2, 2, (int?)1), (revised.Version, revised.LatestVersion, revised.ActiveVersion));

        var active = Assert.Single(await RunAsync(scenario, rules => rules.ListActiveAsync()));
        Assert.Equal((1, RoutingDisposition.Review(1)), (active.Version, active.Definition.Then));

        await RunAsync(scenario, rules => rules.ActivateAsync("summary-changes", 2));
        Assert.Equal(RoutingDisposition.Review(2), Assert.Single(await RunAsync(scenario, rules => rules.ListActiveAsync())).Definition.Then);
        var deactivated = (await RunAsync(scenario, rules => rules.DeactivateAsync("summary-changes"))).Value;

        Assert.Null(deactivated.ActiveVersion);
        Assert.Equal(new RoutingRuleSummary(created.Id, "summary-changes", 2, null), Assert.Single(await RunAsync(scenario, rules => rules.ListAsync())));
        Assert.Equal("Two people.", (await RunAsync(scenario, rules => rules.GetAsync("summary-changes"))).Value.Definition.Description);
        Assert.Equal(SummaryChanges.ToJson(), (await RunAsync(scenario, rules => rules.GetVersionAsync("summary-changes", 1))).Value.Definition.ToJson());

        var versions = await scenario.QueryAsync(context => context.RoutingRuleVersions.AsNoTracking().OrderBy(row => row.Version).Select(row => row.Id).ToListAsync());
        var events = (await scenario.QueryAsync(context => context.AuditEvents.AsNoTracking().Where(row => row.SubjectType == "routing_rule").ToListAsync()))
            .OrderBy(row => row.Timestamp)
            .ToList();
        Assert.Equal(
            ["routing_rule.created", "routing_rule.activated", "routing_rule.revised", "routing_rule.activated", "routing_rule.deactivated"],
            events.Select(row => row.Operation));
        Assert.All(events, row => Assert.Equal((created.Id, Amir.UserId), (row.SubjectId!.Value, row.InitiatorId)));
        Assert.Equal([null, null, versions[0], versions[0], versions[1]], events.Select(row => row.PreviousVersionId));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Expected_failures_say_what_is_wrong_and_write_nothing(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        await RunAsync(scenario, rules => rules.CreateAsync(new NewRoutingRule("summary-changes", SummaryChanges)));
        await RunAsync(scenario, rules => rules.ActivateAsync("summary-changes", 1));
        var events = await RuleEventsAsync(scenario);

        var invalid = await RunAsync(scenario, rules => rules.CreateAsync(new NewRoutingRule("Bad Name", Definition(" ", RoutingConditions.Any, RoutingDisposition.Block))));
        var taken = await RunAsync(scenario, rules => rules.CreateAsync(new NewRoutingRule("summary-changes", NewCategories)));
        var unknown = await RunAsync(scenario, rules => rules.ReviseAsync("nothing", 1, NewCategories));
        var stale = await RunAsync(scenario, rules => rules.ReviseAsync("summary-changes", 2, NewCategories));
        var noVersion = await RunAsync(scenario, rules => rules.ActivateAsync("summary-changes", 3));
        var already = await RunAsync(scenario, rules => rules.ActivateAsync("summary-changes", 1));

        Assert.Equal([RoutingErrorCodes.NameInvalid, RoutingErrorCodes.DescriptionInvalid], invalid.Errors.Select(error => error.Code));
        Assert.Equal(RoutingErrorCodes.NameTaken, Assert.Single(taken.Errors).Code);
        Assert.Equal(RoutingErrorCodes.RuleNotFound, Assert.Single(unknown.Errors).Code);
        Assert.Equal(RoutingErrorCodes.VersionConflict, Assert.Single(stale.Errors).Code);
        Assert.Equal(RoutingErrorCodes.VersionNotFound, Assert.Single(noVersion.Errors).Code);
        Assert.Equal(RoutingErrorCodes.AlreadyActive, Assert.Single(already.Errors).Code);

        await RunAsync(scenario, rules => rules.DeactivateAsync("summary-changes"));
        var inactive = await RunAsync(scenario, rules => rules.DeactivateAsync("summary-changes"));
        Assert.Equal(RoutingErrorCodes.NotActive, Assert.Single(inactive.Errors).Code);
        Assert.Equal(events + 1, await RuleEventsAsync(scenario));
        Assert.Single(await RunAsync(scenario, rules => rules.ListAsync()));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Only_a_person_writes_routing_rules(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        await RunAsync(scenario, rules => rules.CreateAsync(new NewRoutingRule("summary-changes", SummaryChanges)));
        var events = await RuleEventsAsync(scenario);

        foreach (Initiator initiator in new Initiator[] { Agent, SystemInitiator.Checks })
        {
            scenario.Initiator = initiator;
            var refused = new[]
            {
                await RunAsync(scenario, rules => rules.CreateAsync(new NewRoutingRule("new-categories", NewCategories))),
                await RunAsync(scenario, rules => rules.ReviseAsync("summary-changes", 1, NewCategories)),
                await RunAsync(scenario, rules => rules.ActivateAsync("summary-changes", 1)),
                await RunAsync(scenario, rules => rules.DeactivateAsync("summary-changes")),
            };

            Assert.All(refused, result => Assert.Equal(RoutingErrorCodes.PeopleOnly, Assert.Single(result.Errors).Code));
        }

        scenario.Initiator = Amir;
        var stored = Assert.Single(await RunAsync(scenario, rules => rules.ListAsync()));
        Assert.Equal((1, (int?)null), (stored.LatestVersion, stored.ActiveVersion));
        Assert.Equal(events, await RuleEventsAsync(scenario));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_stored_set_routes_by_its_items_their_changed_fields_and_its_initiator(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var product = await scenario.ContentTypeAsync("product", revised: true);
        var category = await scenario.ContentTypeAsync("category");
        var entry = await EntryAsync(scenario, "category", """{"title": "Tools"}""");
        foreach (var (name, definition) in new[] { ("summary-changes", SummaryChanges), ("new-categories", NewCategories) })
        {
            await RunAsync(scenario, rules => rules.CreateAsync(new NewRoutingRule(name, definition)));
            await RunAsync(scenario, rules => rules.ActivateAsync(name, 1));
        }

        await RunAsync(scenario, rules => rules.CreateAsync(new NewRoutingRule("inactive-block", Definition("Never active.", RoutingConditions.Any, RoutingDisposition.Block))));

        var mixed = await scenario.OpenAsync(Create(category), Update(product), Operation(OperationKind.Create, ItemKind.Entry, entry, null, 1));
        var routine = await scenario.OpenAsync(Create(category));
        var events = (await scenario.QueryAsync(context => context.AuditEvents.CountAsync()));

        var mixedDecision = (await RouteAsync(scenario, mixed.Id)).Value;
        var routineDecision = (await RouteAsync(scenario, routine.Id)).Value;
        var unknown = await RouteAsync(scenario, Guid.NewGuid());

        Assert.Equal(RoutingDisposition.Review(1), mixedDecision.Disposition);
        Assert.Equal(["new-categories", "summary-changes"], mixedDecision.Rules.Select(rule => rule.Name));
        Assert.Equal([category.Id, entry], mixedDecision.Rules[0].Items);
        Assert.Equal([product.Id], mixedDecision.Rules[1].Items);
        Assert.Empty(mixedDecision.UnmatchedItems);

        Assert.Equal(RoutingDisposition.AutoPublish, routineDecision.Disposition);
        Assert.Equal(ChangeSetErrorCodes.ChangeSetNotFound, Assert.Single(unknown.Errors).Code);
        Assert.Equal(events, await scenario.QueryAsync(context => context.AuditEvents.CountAsync()));

        // The same routine set opened by an agent is no longer routine, since the rule names people.
        scenario.Initiator = Agent;
        var byAgent = await scenario.OpenAsync(Create(category));
        var agentDecision = (await RouteAsync(scenario, byAgent.Id)).Value;
        Assert.Null(agentDecision.Disposition);
        Assert.Equal([category.Id], agentDecision.UnmatchedItems);
    }

    // Routing rules are a person's to write, so these tests act as one unless they say otherwise.
    private static async Task<ChangeSetScenario> ScenarioAsync(string provider)
    {
        var scenario = await ChangeSetScenario.CreateAsync(provider);
        scenario.Initiator = Amir;
        return scenario;
    }

    private static async Task<T> RunAsync<T>(ChangeSetScenario scenario, Func<IRoutingRuleDirectory, Task<T>> action)
    {
        await using var scope = scenario.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IRoutingRuleDirectory>());
    }

    private static async Task<Core.Results.Result<RoutingDecision>> RouteAsync(ChangeSetScenario scenario, Guid changeSetId)
    {
        await using var scope = scenario.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IChangeSetRouting>().RouteAsync(changeSetId);
    }

    private static async Task<Guid> EntryAsync(ChangeSetScenario scenario, string contentType, string fields)
    {
        await using var scope = scenario.Services.CreateAsyncScope();
        using var document = JsonDocument.Parse(fields);
        var entries = scope.ServiceProvider.GetRequiredService<IEntryDirectory>();

        return (await entries.CreateAsync(new NewEntry(contentType, document.RootElement.Clone()))).Value.Id;
    }

    private static Task<int> RuleEventsAsync(ChangeSetScenario scenario)
    {
        return scenario.QueryAsync(context => context.AuditEvents.CountAsync(row => row.SubjectType == "routing_rule"));
    }

    private static ChangeOperation Operation(OperationKind kind, ItemKind itemKind, Guid itemId, int? baseVersion, int? proposedVersion)
    {
        return ChangeOperation.Create(kind, itemKind, itemId, baseVersion, proposedVersion).Value;
    }

    private static RoutingRuleDefinition Definition(string description, RoutingConditions when, RoutingDisposition then)
    {
        return new RoutingRuleDefinition(description, when, then);
    }
}
