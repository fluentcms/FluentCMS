using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.Agents;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Content;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Tools;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

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

        var events = await agent.QueryAsync(context => Task.FromResult(context.AuditEvents.Count()));

        var review = await agent.CallAsync("get_change_set_review", new() { ["id"] = set.ToString() });
        var refused = await agent.FailAsync("get_change_set_review", new() { ["id"] = someoneElses.ToString() });

        Assert.Equal(("rejected", "The type duplicates item."), ((string?)review["state"], (string?)review["rejectionReason"]));
        var moves = review["moves"]!.AsArray();
        Assert.Equal(["opened", "submitted", "awaiting_review", "rejected"], moves.Select(move => (string?)move!["move"]));
        Assert.Equal(("agent", "schema-agent"), ((string?)moves[0]!["by"]!["kind"], (string?)moves[0]!["by"]!["name"]));
        Assert.Equal(("user", "Test administrator"), ((string?)moves[3]!["by"]!["kind"], (string?)moves[3]!["by"]!["name"]));
        Assert.Equal((string?)moves[3]!["at"], (string?)review["stateSince"]);
        Assert.Equal(AgentPermissionErrorCodes.NotOwner, Code(refused));
        Assert.Equal(events, await agent.QueryAsync(context => Task.FromResult(context.AuditEvents.Count())));
    }

    // The credential id separates one agent from another; a person's sets are already left out by the initiator's kind.
    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Another_agents_sets_are_not_counted_listed_or_readable(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider);
        await using var other = await agent.AnotherAgentAsync("other-agent");
        var mine = await SubmitAsync(agent, "product");
        var theirs = await SubmitAsync(other, "category");
        var theirsFailed = await SubmitAsync(other, "brand", submit: false);
        await MoveAsync(agent, theirsFailed, ChangeSetState.ChecksRunning);
        await MoveAsync(agent, theirsFailed, ChangeSetState.ChecksFailed);

        var counts = await agent.CallAsync("get_desk_counts", []);
        var awaiting = await agent.CallAsync("list_desk_queue", new() { ["queue"] = "awaiting_review" });
        var failed = await agent.CallAsync("list_desk_queue", new() { ["queue"] = "checks_failed" });
        var refused = await agent.FailAsync("get_change_set_review", new() { ["id"] = theirs.ToString() });
        var theirCounts = await other.CallAsync("get_desk_counts", []);

        Assert.Equal((1, 0), ((int)counts["counts"]!["awaiting_review"]!, (int)counts["counts"]!["checks_failed"]!));
        Assert.Equal([mine.ToString()], Ids(awaiting));
        Assert.Empty(Ids(failed));
        Assert.Equal(AgentPermissionErrorCodes.NotOwner, Code(refused));
        Assert.Equal((1, 1), ((int)theirCounts["counts"]!["awaiting_review"]!, (int)theirCounts["counts"]!["checks_failed"]!));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Every_queue_lists_through_the_tool_in_its_order_and_window_and_no_call_appends_an_event(string provider)
    {
        var clock = new ManualClock(DateTimeOffset.UtcNow);
        await using var agent = await ConnectedAgent.StartAsync(provider, configure: builder => builder.Services.AddSingleton<TimeProvider>(clock));
        var old = await PublishAsync(agent, await SubmitAsync(agent, "region"));
        var oldRevert = await RevertAsync(agent, await PublishAsync(agent, await SubmitAsync(agent, "warehouse")));
        clock.Now = clock.Now.AddDays(8);
        var first = await PublishAsync(agent, await SubmitAsync(agent, "product"));
        clock.Now = clock.Now.AddMinutes(1);
        var wrong = await RevertAsync(agent, await PublishAsync(agent, await SubmitAsync(agent, "category")));
        clock.Now = clock.Now.AddMinutes(1);
        var alsoWrong = await RevertAsync(agent, await PublishAsync(agent, await SubmitAsync(agent, "colour")));
        clock.Now = clock.Now.AddMinutes(1);
        var second = await PublishAsync(agent, await SubmitAsync(agent, "brand"));
        var failedFirst = await FailedAsync(agent, "size");
        clock.Now = clock.Now.AddMinutes(1);
        var failedLater = await FailedAsync(agent, "shape");
        var stale = await StaleFactsAsync(agent, 3);
        var events = await agent.QueryAsync(context => Task.FromResult(context.AuditEvents.Count()));

        var counts = await agent.CallAsync("get_desk_counts", []);
        var published = await agent.CallAsync("list_desk_queue", new() { ["queue"] = "recently_published" });
        var reverted = await agent.CallAsync("list_desk_queue", new() { ["queue"] = "reverted" });
        var failed = await agent.CallAsync("list_desk_queue", new() { ["queue"] = "checks_failed" });
        var staleFirst = await agent.CallAsync("list_desk_queue", new() { ["queue"] = "stale_facts", ["limit"] = 2 });
        var staleSecond = await agent.CallAsync("list_desk_queue", new() { ["queue"] = "stale_facts", ["limit"] = 2, ["page"] = 2 });
        await agent.CallAsync("get_change_set_review", new() { ["id"] = wrong.ToString() });

        Assert.Equal(
            (2, 2, 2, 3),
            ((int)counts["counts"]!["recently_published"]!, (int)counts["counts"]!["reverted"]!, (int)counts["counts"]!["checks_failed"]!, (int)counts["counts"]!["stale_facts"]!));
        Assert.Equal([second.ToString(), first.ToString()], Ids(published));
        Assert.DoesNotContain(old.ToString(), Ids(published));
        Assert.Equal([alsoWrong.ToString(), wrong.ToString()], Ids(reverted));
        Assert.DoesNotContain(oldRevert.ToString(), Ids(reverted));
        Assert.Equal([failedFirst.ToString(), failedLater.ToString()], Ids(failed));
        Assert.Equal((3, 3), ((int)staleFirst["total"]!, (int)staleSecond["total"]!));
        Assert.Equal(
            stale,
            [.. staleFirst["facts"]!.AsArray().Concat(staleSecond["facts"]!.AsArray()).Select(fact => Guid.Parse((string)fact!["entityId"]!))]);
        Assert.Equal(events, await agent.QueryAsync(context => Task.FromResult(context.AuditEvents.Count())));
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

    // A person reverts the published set and publishes the revert, which moves the set to reverted.
    private static async Task<Guid> RevertAsync(ConnectedAgent agent, Guid id)
    {
        var revert = await agent.AsPersonAsync<IChangeSetDirectory, ChangeSet>(directory => directory.RevertAsync(id, "It was a mistake."));
        await MoveAsync(agent, revert.Id, ChangeSetState.ChecksRunning);
        await MoveAsync(agent, revert.Id, ChangeSetState.AwaitingReview);
        await PublishAsync(agent, revert.Id);
        return id;
    }

    private static async Task<Guid> FailedAsync(ConnectedAgent agent, string slug)
    {
        var id = await SubmitAsync(agent, slug, submit: false);
        await MoveAsync(agent, id, ChangeSetState.ChecksRunning);
        await MoveAsync(agent, id, ChangeSetState.ChecksFailed);
        return id;
    }

    // A person approves and publishes the set, as a reviewer would.
    private static async Task<Guid> PublishAsync(ConnectedAgent agent, Guid id)
    {
        await MoveAsync(agent, id, ChangeSetState.Approved);
        await agent.AsPersonAsync<IChangeSetDirectory, ChangeSet>(directory => directory.PublishAsync(id));
        return id;
    }

    // Suppliers whose price expired a day apart, so the longest stale comes first; returns them in that order.
    private static async Task<List<Guid>> StaleFactsAsync(ConnectedAgent agent, int count)
    {
        await agent.AsPersonAsync<IContentTypeDirectory, ContentType>(directory => directory.CreateAsync(
            new NewContentType("Supplier", "supplier", new ContentTypeDefinition([new FieldDefinition("name", "text"), new FieldDefinition("price", "number")], isEntity: true)),
            TestChangeSetTargets.ContentModel));
        var ids = new List<Guid>();
        for (var day = 1; day <= count; day++)
        {
            const string sourced = """{"kind": "url", "reference": "https://example.com/catalog"}""";
            var fields = JsonSerializer.Deserialize<JsonElement>($$"""
                {
                  "name": {"value": "Supplier {{day}}", "source": {{sourced}}, "owner": null, "validFrom": null, "validUntil": null, "lastVerified": null},
                  "price": {"value": 99, "source": {{sourced}}, "owner": null, "validFrom": null, "validUntil": "2020-01-0{{day}}T00:00:00Z", "lastVerified": null}
                }
                """);
            ids.Add((await agent.AsPersonAsync<IEntryDirectory, Entry>(directory => directory.CreateAsync(new NewEntry("supplier", fields)))).Id);
        }

        return ids;
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
