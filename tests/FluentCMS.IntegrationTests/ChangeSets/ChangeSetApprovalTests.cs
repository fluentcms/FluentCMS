using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Identity;
using FluentCMS.Infrastructure.ChangeSets;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static FluentCMS.IntegrationTests.ChangeSets.ChangeSetScenario;

namespace FluentCMS.IntegrationTests.ChangeSets;

// A set in review needs as many approvals as it asks for, each by a different person (RTE-05).
public sealed class ChangeSetApprovalTests
{
    private static readonly UserInitiator Dana = new(Guid.NewGuid(), "Dana");

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task With_the_default_of_one_an_approval_approves_the_set_and_is_audited(string provider)
    {
        await using var scenario = await ScenarioAsync(provider);
        var set = await InReviewAsync(scenario);

        var approved = await scenario.RunAsync(directory => directory.ApproveAsync(set.Id));

        Assert.Equal(ChangeSetState.Approved, approved.Value.State);
        Assert.Equal(1, approved.Value.RequiredApprovals);
        Assert.Equal(Amir.UserId, Assert.Single(approved.Value.Approvals).Approver.UserId);
        Assert.Equal(["change_set.approval_recorded", "change_set.approved"], (await EventsAsync(scenario, set.Id))[^2..]);
        Assert.Equal(ChangeSetState.Approved, (await scenario.RunAsync(directory => directory.GetAsync(set.Id))).Value.State);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_set_that_needs_two_approvals_waits_for_a_second_person(string provider)
    {
        await using var scenario = await ScenarioAsync(provider, required: 2);
        var set = await InReviewAsync(scenario);
        var events = (await EventsAsync(scenario, set.Id)).Count;

        var first = await scenario.RunAsync(directory => directory.ApproveAsync(set.Id));
        var again = await scenario.RunAsync(directory => directory.TransitionAsync(set.Id, ChangeSetState.Approved));
        scenario.Initiator = Dana;
        var second = await scenario.RunAsync(directory => directory.ApproveAsync(set.Id));

        Assert.Equal((ChangeSetState.AwaitingReview, 2), (first.Value.State, first.Value.RequiredApprovals));
        Assert.Equal(ChangeSetErrorCodes.ApprovalDuplicate, Assert.Single(again.Errors).Code);
        Assert.Equal(ChangeSetState.Approved, second.Value.State);
        Assert.Equal([Amir.UserId, Dana.UserId], second.Value.Approvals.Select(approval => approval.Approver.UserId));
        Assert.Equal(2, await scenario.QueryAsync(context => context.ChangeSetApprovals.CountAsync(row => row.ChangeSetId == set.Id)));
        Assert.Equal(
            ["change_set.approval_recorded", "change_set.approval_recorded", "change_set.approved"],
            (await EventsAsync(scenario, set.Id)).Skip(events));
    }

    // A person's approval while the set is being checked, or after it was approved, is no approval at all.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Only_a_set_in_review_takes_approvals(string provider)
    {
        await using var scenario = await ScenarioAsync(provider, required: 2);
        var set = await InReviewAsync(scenario);
        scenario.Initiator = Dana;
        await scenario.RunAsync(directory => directory.ApproveAsync(set.Id));
        scenario.Initiator = Amir;
        await scenario.RunAsync(directory => directory.ApproveAsync(set.Id));
        var events = (await EventsAsync(scenario, set.Id)).Count;

        var late = await scenario.RunAsync(directory => directory.ApproveAsync(set.Id));
        var unknown = await scenario.RunAsync(directory => directory.ApproveAsync(Guid.NewGuid()));

        Assert.Equal(ChangeSetErrorCodes.TransitionNotAllowed, Assert.Single(late.Errors).Code);
        Assert.Equal(ChangeSetErrorCodes.ChangeSetNotFound, Assert.Single(unknown.Errors).Code);
        Assert.Equal(events, (await EventsAsync(scenario, set.Id)).Count);
    }

    private static async Task<ChangeSetScenario> ScenarioAsync(string provider, int? required = null)
    {
        var scenario = await ChangeSetScenario.CreateAsync(
            provider,
            services => services.Configure<ApprovalOptions>(options => options.Required = required ?? options.Required));
        scenario.Initiator = Amir;
        return scenario;
    }

    private static async Task<ChangeSet> InReviewAsync(ChangeSetScenario scenario)
    {
        var set = await scenario.OpenAsync(Create(await scenario.ContentTypeAsync("product")));
        await scenario.RunAsync(directory => directory.TransitionAsync(set.Id, ChangeSetState.ChecksRunning));
        return (await scenario.RunAsync(directory => directory.TransitionAsync(set.Id, ChangeSetState.AwaitingReview))).Value;
    }

    private static async Task<List<string>> EventsAsync(ChangeSetScenario scenario, Guid changeSetId)
    {
        return [.. (await scenario.ChangeSetEventsAsync(changeSetId)).Select(row => row.Operation)];
    }
}
