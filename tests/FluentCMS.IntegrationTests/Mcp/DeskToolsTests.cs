using System.Text.Json.Nodes;
using FluentCMS.Core.Agents;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Tools;
using FluentCMS.Infrastructure.Persistence;

namespace FluentCMS.IntegrationTests.Mcp;

public sealed class DeskToolsTests
{
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_agent_reads_the_desk_queues_and_counts_of_its_own_sets(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider);
        var first = await SubmitAsync(agent, "product");
        var second = await SubmitAsync(agent, "category");
        var failed = await SubmitAsync(agent, "brand", submit: false);
        await MoveAsync(agent, failed, ChangeSetState.ChecksRunning);
        await MoveAsync(agent, failed, ChangeSetState.ChecksFailed);
        var someoneElses = await PersonsSetAsync(agent, "page");
        var events = await agent.QueryAsync(context => Task.FromResult(context.AuditEvents.Count()));

        var counts = await agent.CallAsync("get_desk_counts", []);
        var awaiting = await agent.CallAsync("list_desk_queue", new() { ["queue"] = "awaiting_review" });
        var page = await agent.CallAsync("list_desk_queue", new() { ["queue"] = "awaiting_review", ["page"] = 2, ["limit"] = 1 });
        var checksFailed = await agent.CallAsync("list_desk_queue", new() { ["queue"] = "checks_failed" });
        var stale = await agent.CallAsync("list_desk_queue", new() { ["queue"] = "stale_facts" });

        Assert.Equal(
            ["awaiting_review", "checks_failed", "stale_facts", "recently_published", "reverted"],
            counts["counts"]!.AsObject().Select(pair => pair.Key));
        Assert.Equal((2, 1, 0), ((int)counts["counts"]!["awaiting_review"]!, (int)counts["counts"]!["checks_failed"]!, (int)counts["counts"]!["stale_facts"]!));
        Assert.Equal([first.ToString(), second.ToString()], Ids(awaiting));
        Assert.DoesNotContain(someoneElses.ToString(), Ids(awaiting));
        Assert.Equal(2, (int)awaiting["total"]!);
        var listed = awaiting["changeSets"]![0]!;
        Assert.Equal(
            ("awaiting_review", "Add the product type.", "agent", "schema-agent", 1),
            ((string?)listed["state"], (string?)listed["rationale"], (string?)listed["initiator"]!["kind"], (string?)listed["initiator"]!["name"], (int)listed["items"]!));
        Assert.Equal([second.ToString()], Ids(page));
        Assert.Equal([failed.ToString()], Ids(checksFailed));
        Assert.Empty(stale["facts"]!.AsArray());
        Assert.Equal(events, await agent.QueryAsync(context => Task.FromResult(context.AuditEvents.Count())));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_agent_reads_who_rejected_its_set_and_why_and_not_the_review_of_another_set(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider);
        var set = await SubmitAsync(agent, "product");
        await agent.AsPersonAsync<IChangeSetDirectory, ChangeSet>(directory => directory.RejectAsync(set, "The type duplicates item."));
        var someoneElses = await PersonsSetAsync(agent, "page");

        var review = await agent.CallAsync("get_change_set_review", new() { ["id"] = set.ToString() });
        var refused = await agent.FailAsync("get_change_set_review", new() { ["id"] = someoneElses.ToString() });

        Assert.Equal(("rejected", "The type duplicates item."), ((string?)review["state"], (string?)review["rejectionReason"]));
        var moves = review["moves"]!.AsArray();
        Assert.Equal(["opened", "submitted", "awaiting_review", "rejected"], moves.Select(move => (string?)move!["move"]));
        Assert.Equal(("agent", "schema-agent"), ((string?)moves[0]!["by"]!["kind"], (string?)moves[0]!["by"]!["name"]));
        Assert.Equal(("user", "Test administrator"), ((string?)moves[3]!["by"]!["kind"], (string?)moves[3]!["by"]!["name"]));
        Assert.Equal((string?)moves[3]!["at"], (string?)review["stateSince"]);
        Assert.Equal(AgentPermissionErrorCodes.NotOwner, Code(refused));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_agent_reads_who_approved_its_set_and_how_many_approvals_it_needs(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider);
        var set = await SubmitAsync(agent, "product");
        await agent.AsPersonAsync<IChangeSetDirectory, ChangeSet>(directory => directory.ApproveAsync(set));

        var review = await agent.CallAsync("get_change_set_review", new() { ["id"] = set.ToString() });

        Assert.Equal(("approved", 1), ((string?)review["state"], (int)review["requiredApprovals"]!));
        var approval = Assert.Single(review["approvals"]!.AsArray());
        Assert.Equal(("user", "Test administrator"), ((string?)approval!["by"]!["kind"], (string?)approval["by"]!["name"]));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Bad_arguments_and_an_unknown_set_are_refused_with_what_to_do(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider);

        var unknownQueue = await agent.FailAsync("list_desk_queue", new() { ["queue"] = "awaiting_me" });
        var tooMany = await agent.FailAsync("list_desk_queue", new() { ["queue"] = "reverted", ["limit"] = 500 });
        var zeroPage = await agent.FailAsync("list_desk_queue", new() { ["queue"] = "reverted", ["page"] = 0 });
        var unknownSet = await agent.FailAsync("get_change_set_review", new() { ["id"] = Guid.NewGuid().ToString() });

        Assert.Equal(ToolErrorCodes.ArgumentInvalid, Code(unknownQueue));
        Assert.Contains("awaiting_review", Message(unknownQueue));
        Assert.Equal(ToolErrorCodes.ArgumentInvalid, Code(tooMany));
        Assert.Equal(ToolErrorCodes.ArgumentInvalid, Code(zeroPage));
        Assert.Equal(ChangeSetErrorCodes.ChangeSetNotFound, Code(unknownSet));
        Assert.Contains("list_desk_queue", unknownSet.ToJsonString());
    }

    private static string? Code(JsonObject failure)
    {
        return (string?)failure["errors"]![0]!["code"];
    }

    private static string Message(JsonObject failure)
    {
        return (string?)failure["errors"]![0]!["message"] ?? string.Empty;
    }

    private static List<string?> Ids(JsonObject list)
    {
        return [.. list["changeSets"]!.AsArray().Select(set => (string?)set!["id"])];
    }

    // A set the agent opens and submits through its tools, proposing one new content type. No check reads a content type, so the
    // set passes its checks and waits for review; one left unsubmitted is moved on by a person, standing in for the engine.
    private static async Task<Guid> SubmitAsync(ConnectedAgent agent, string slug, bool submit = true)
    {
        var opened = await agent.CallAsync("open_change_set", new() { ["rationale"] = $"Add the {slug} type." });
        var id = (string)opened["changeSet"]!["id"]!;
        await agent.CallAsync("create_content_type", new()
        {
            ["name"] = slug,
            ["slug"] = slug,
            ["definition"] = new JsonObject { ["fields"] = new JsonArray(new JsonObject { ["name"] = "title", ["type"] = "text" }) },
            ["change_set_id"] = id,
        });
        if (submit)
        {
            await agent.CallAsync("submit_change_set", new() { ["id"] = id });
        }

        return Guid.Parse(id);
    }

    // The checks and the reviewer move a set on; a person stands in for the engine here.
    private static Task<ChangeSet> MoveAsync(ConnectedAgent agent, Guid id, ChangeSetState state)
    {
        return agent.AsPersonAsync<IChangeSetDirectory, ChangeSet>(directory => directory.TransitionAsync(id, state));
    }

    private static async Task<Guid> PersonsSetAsync(ConnectedAgent agent, string slug)
    {
        var type = await agent.CreateAsPersonAsync(slug);
        var set = await agent.AsPersonAsync<IChangeSetDirectory, ChangeSet>(directory => directory.OpenAsync(new NewChangeSet($"Add the {slug} type.", [])));
        await agent.AsPersonAsync<IChangeSetDirectory, ChangeSet>(directory =>
            directory.AddOperationAsync(set.Id, ChangeOperation.Create(OperationKind.Create, ItemKind.Schema, type.Id, null, 1).Value));
        await MoveAsync(agent, set.Id, ChangeSetState.ChecksRunning);
        await MoveAsync(agent, set.Id, ChangeSetState.AwaitingReview);
        return set.Id;
    }
}
