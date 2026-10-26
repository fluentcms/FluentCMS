using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.Agents;
using FluentCMS.Core.Builtin.Jobs;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Content;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Tools;
using FluentCMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.IntegrationTests.Mcp;

public sealed class BulkToolsTests
{
    private static readonly Dictionary<string, string?> FastQueue = new()
    {
        ["JobQueue:PollingInterval"] = "00:00:00.050",
        ["JobQueue:RetryDelay"] = "00:00:00.050",
    };

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_bulk_update_of_180_entities_returns_the_change_set_at_once_and_reports_progress_until_it_is_ready(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider, settings: FastQueue);
        var products = await CreateProductsAsync(agent, 180);

        var started = await agent.CallAsync("bulk_update_entries", new()
        {
            ["items"] = products.Select(id => Item(id, 1, 29.99m)).ToArray(),
            ["rationale"] = "Sync prices from the catalog export.",
            ["sources"] = new[] { "exports/catalog-2026-10-19.csv" },
        });
        var changeSetId = (string)started["changeSet"]!["id"]!;
        var jobId = (string)started["job"]!["id"]!;
        var seen = new List<int>();
        var finished = await FinishedAsync(agent, jobId, seen);

        Assert.Equal("open", (string?)started["changeSet"]!["state"]);
        Assert.Equal(("queued", 0), ((string?)started["job"]!["state"], (int)started["job"]!["progress"]!["done"]!));
        Assert.Equal(changeSetId, (string?)started["job"]!["subject"]!["id"]);
        Assert.Equal(seen.Order(), seen);
        Assert.Equal("succeeded", (string?)finished["state"]);
        Assert.Equal((180, 180), ((int)finished["progress"]!["done"]!, (int)finished["progress"]!["total"]!));
        Assert.Equal("Updated 180 of 180.", (string?)finished["progress"]!["message"]);

        var set = await agent.AsPersonAsync<IChangeSetDirectory, ChangeSet>(directory => directory.GetAsync(Guid.Parse(changeSetId)));
        Assert.Equal(180, set.Operations.Count);
        Assert.All(set.Operations, operation => Assert.Equal((ItemKind.Entity, 2), (operation.ItemKind, operation.ProposedVersion!.Value)));
        Assert.Equal(agent.Credential.Initiator, set.Initiator);

        var submitted = await agent.CallAsync("submit_change_set", new() { ["id"] = changeSetId });
        Assert.Equal("checks_running", (string?)submitted["changeSet"]!["state"]);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task An_item_that_fails_is_listed_by_its_id_and_the_rest_go_into_the_set(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider, settings: FastQueue);
        var products = await CreateProductsAsync(agent, 4);
        // Plain values where the entity's fields take facts: one item with a problem in every field.
        var withoutFacts = new Dictionary<string, object?> { ["id"] = products[2].ToString(), ["base_version"] = 1, ["fields"] = new { name = "Widget", price = 3 } };

        var started = await agent.CallAsync("bulk_update_entries", new()
        {
            ["items"] = new[] { Item(products[0], 1, 1m), Item(products[1], 5, 2m), withoutFacts, Item(products[3], 1, 4m) },
            ["rationale"] = "Sync prices.",
        });
        var finished = await FinishedAsync(agent, (string)started["job"]!["id"]!);

        Assert.Equal("failed", (string?)finished["state"]);
        Assert.Equal(1, (int)finished["attempts"]!);
        Assert.StartsWith("bulk_items_failed: 2 of 4 items failed. ", (string?)finished["lastError"], StringComparison.Ordinal);
        Assert.Contains($"items[1] {products[1]}", (string?)finished["lastError"]);
        Assert.Contains($"items[2] {products[2]}", (string?)finished["lastError"]);
        Assert.Equal("Updated 2 of 4; 2 failed.", (string?)finished["progress"]!["message"]);
        var set = await agent.AsPersonAsync<IChangeSetDirectory, ChangeSet>(directory => directory.GetAsync(Guid.Parse((string)started["changeSet"]!["id"]!)));
        Assert.Equal([products[0], products[3]], set.Operations.Select(operation => operation.ItemId));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Many_failed_items_keep_their_count_and_the_first_twenty_in_the_jobs_last_error(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider, settings: FastQueue);
        var products = await CreateProductsAsync(agent, 30);

        var started = await agent.CallAsync("bulk_update_entries", new()
        {
            ["items"] = products.Select(id => new Dictionary<string, object?> { ["id"] = id.ToString(), ["base_version"] = 1, ["fields"] = new { name = "Widget", price = 3 } }).ToArray(),
            ["rationale"] = "Sync prices.",
        });
        var finished = await FinishedAsync(agent, (string)started["job"]!["id"]!);

        var lastError = (string)finished["lastError"]!;
        Assert.StartsWith("bulk_items_failed: 30 of 30 items failed; the first 20 are listed. ", lastError, StringComparison.Ordinal);
        Assert.Contains($"items[19] {products[19]}", lastError);
        Assert.DoesNotContain("items[20]", lastError);
        Assert.Equal("Updated 0 of 30; 30 failed.", (string?)finished["progress"]!["message"]);
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_list_past_the_cap_or_with_a_malformed_item_is_refused_before_a_set_is_opened(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider, itemsPerChangeSet: 2, settings: FastQueue);
        var products = await CreateProductsAsync(agent, 3);

        var overCap = await agent.FailAsync("bulk_update_entries", new()
        {
            ["items"] = products.Select(id => Item(id, 1, 1m)).ToArray(),
            ["rationale"] = "Too many.",
        });
        var malformed = await agent.FailAsync("bulk_update_entries", new()
        {
            ["items"] = new object[] { Item(products[0], 1, 1m), new Dictionary<string, object?> { ["id"] = "42", ["fields"] = new { } }, Item(products[0], 1, 1m) },
            ["rationale"] = "Malformed.",
        });

        Assert.Equal(AgentPermissionErrorCodes.CapExceeded, (string?)overCap["errors"]![0]!["code"]);
        Assert.StartsWith("3 items are more than", (string?)overCap["errors"]![0]!["message"], StringComparison.Ordinal);
        var refusal = Assert.Single(await agent.QueryAsync(context => context.AuditEvents
            .AsNoTracking()
            .Where(row => row.Operation == "agent.items_per_change_set_exceeded")
            .ToListAsync()));
        Assert.Equal(("agent_credential", agent.Credential.Id), (refusal.SubjectType, refusal.SubjectId));
        Assert.Equal(
            [(ToolErrorCodes.ArgumentInvalid, "items[1]"), (ToolErrorCodes.ArgumentMissing, "items[1]"), (ToolErrorCodes.ArgumentInvalid, "items[2]")],
            malformed["errors"]!.AsArray().Select(error => ((string)error!["code"]!, ((string)error["message"]!).Split(':')[0])));
        // The only set is the one the test's content model was written in.
        Assert.Empty(await agent.QueryAsync(context => context.ChangeSets
            .AsNoTracking()
            .Where(row => row.SessionId != TestChangeSetTargets.ContentModelSession)
            .ToListAsync()));
        Assert.Empty(await agent.QueryAsync(context => context.Jobs.AsNoTracking().Where(row => row.Type == BulkEntryUpdates.Name).ToListAsync()));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_job_run_again_after_stopping_partway_adds_each_item_once(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider);
        var products = await CreateProductsAsync(agent, 3);
        var set = await agent.AsAgentAsync(async services => (await services.GetRequiredService<IChangeSetDirectory>().OpenAsync(new NewChangeSet("Sync prices.", []))).Value);
        var payload = BulkEntryUpdates.Payload(set.Id, [.. products.Select(id => new BulkEntryUpdate(id, 1, Fields(5m)))]);
        var job = new Job
        {
            Id = Guid.NewGuid(),
            Type = BulkEntryUpdates.Name,
            Payload = payload,
            State = JobState.Running,
            Attempts = 1,
            MaxAttempts = 3,
            CreatedAt = DateTimeOffset.UtcNow,
            ScheduledAt = DateTimeOffset.UtcNow,
        };

        // The first run stops after the first item, as a worker that lost its lease would; the second runs the whole job.
        var first = await agent.AsAgentAsync(services =>
            new BulkEntryUpdates().RunAsync(new JobContext(job, services, (progress, _) => Task.FromResult(progress.Done < 1)), CancellationToken.None));
        var second = await agent.AsAgentAsync(services =>
            new BulkEntryUpdates().RunAsync(new JobContext(job, services, (_, _) => Task.FromResult(true)), CancellationToken.None));

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        var stored = await agent.AsPersonAsync<IChangeSetDirectory, ChangeSet>(directory => directory.GetAsync(set.Id));
        Assert.Equal(products, stored.Operations.Select(operation => operation.ItemId));
        foreach (var id in products)
        {
            Assert.Equal(2, (await agent.AsPersonAsync<IEntryDirectory, Entry>(entries => entries.GetAsync(id))).Latest.Version);
        }
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_job_that_fails_partway_is_retried_by_the_worker_and_adds_each_item_once(string provider)
    {
        var failed = 0;
        var handler = new SteppedBulkUpdates((context, run) => Interlocked.Exchange(ref failed, 1) == 0
            ? run(new JobContext(context.Job, context.Services, (progress, token) => progress.Done == 2
                ? throw new InvalidOperationException("The host stopped after the second item.")
                : context.ReportProgressAsync(progress.Done, progress.Total, progress.Message, token)))
            : run(context));
        await using var agent = await ConnectedAgent.StartAsync(provider, settings: FastQueue, configure: handler.Register);
        var products = await CreateProductsAsync(agent, 3);

        var started = await agent.CallAsync("bulk_update_entries", new()
        {
            ["items"] = products.Select(id => Item(id, 1, 5m)).ToArray(),
            ["rationale"] = "Sync prices.",
        });
        var finished = await FinishedAsync(agent, (string)started["job"]!["id"]!);

        Assert.Equal(("succeeded", 2), ((string?)finished["state"], (int)finished["attempts"]!));
        Assert.Equal("Updated 3 of 3.", (string?)finished["progress"]!["message"]);
        var set = await agent.AsPersonAsync<IChangeSetDirectory, ChangeSet>(directory => directory.GetAsync(Guid.Parse((string)started["changeSet"]!["id"]!)));
        Assert.Equal(products, set.Operations.Select(operation => operation.ItemId));
        foreach (var id in products)
        {
            Assert.Equal(2, (await agent.AsPersonAsync<IEntryDirectory, Entry>(entries => entries.GetAsync(id))).Latest.Version);
        }
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task A_set_is_not_submitted_while_its_bulk_job_is_still_filling_it(string provider)
    {
        var release = new TaskCompletionSource();
        var handler = new SteppedBulkUpdates(async (context, run) =>
        {
            await release.Task;
            return await run(context);
        });
        await using var agent = await ConnectedAgent.StartAsync(provider, settings: FastQueue, configure: handler.Register);
        var products = await CreateProductsAsync(agent, 2);
        var started = await agent.CallAsync("bulk_update_entries", new()
        {
            ["items"] = products.Select(id => Item(id, 1, 5m)).ToArray(),
            ["rationale"] = "Sync prices.",
        });
        var changeSetId = (string)started["changeSet"]!["id"]!;

        var early = await agent.FailAsync("submit_change_set", new() { ["id"] = changeSetId });
        release.SetResult();
        var finished = await FinishedAsync(agent, (string)started["job"]!["id"]!);
        var submitted = await agent.CallAsync("submit_change_set", new() { ["id"] = changeSetId });

        Assert.Equal(ChangeSetErrorCodes.Busy, (string?)early["errors"]![0]!["code"]);
        Assert.Equal("succeeded", (string?)finished["state"]);
        Assert.NotEqual("open", (string?)submitted["changeSet"]!["state"]);
        var set = await agent.AsPersonAsync<IChangeSetDirectory, ChangeSet>(directory => directory.GetAsync(Guid.Parse(changeSetId)));
        Assert.Equal(products, set.Operations.Select(operation => operation.ItemId));
    }

    [Theory]
    [InlineData(DatabaseProviders.Sqlite)]
    [PostgresData]
    public async Task Get_job_shows_an_agent_its_own_jobs_only(string provider)
    {
        await using var agent = await ConnectedAgent.StartAsync(provider, settings: FastQueue);
        await using var another = await agent.AnotherAgentAsync("another-agent");
        var products = await CreateProductsAsync(agent, 2);
        var started = await agent.CallAsync("bulk_update_entries", new() { ["items"] = new[] { Item(products[0], 1, 1m) }, ["rationale"] = "One price." });
        var anothers = await another.CallAsync("bulk_update_entries", new() { ["items"] = new[] { Item(products[1], 1, 2m) }, ["rationale"] = "Another price." });

        var own = await agent.CallAsync("get_job", new() { ["id"] = (string)started["job"]!["id"]! });
        var other = await agent.FailAsync("get_job", new() { ["id"] = (string)anothers["job"]!["id"]! });

        Assert.Equal(BulkEntryUpdates.Name, (string?)own["type"]);
        Assert.Equal(JobErrorCodes.JobNotFound, (string?)other["errors"]![0]!["code"]);
    }

    private static async Task<List<Guid>> CreateProductsAsync(ConnectedAgent agent, int count)
    {
        await agent.AsPersonAsync<IContentTypeDirectory, ContentType>(directory => directory.CreateAsync(new NewContentType(
            "Product",
            "product",
            new ContentTypeDefinition([new FieldDefinition("name", "text"), new FieldDefinition("price", "number")], isEntity: true)),
            TestChangeSetTargets.ContentModel));

        var ids = new List<Guid>();
        for (var index = 0; index < count; index++)
        {
            var entry = await agent.AsPersonAsync<IEntryDirectory, Entry>(entries => entries.CreateAsync(new NewEntry("product", Fields(10m + index))));
            ids.Add(entry.Id);
        }

        return ids;
    }

    private static Dictionary<string, object?> Item(Guid id, int baseVersion, decimal price)
    {
        return new() { ["id"] = id.ToString(), ["base_version"] = baseVersion, ["fields"] = Fields(price) };
    }

    private static JsonElement Fields(decimal price)
    {
        return JsonDocument.Parse($$"""{"name": {{Fact("\"Widget\"")}}, "price": {{Fact(price.ToString(System.Globalization.CultureInfo.InvariantCulture))}}}""").RootElement.Clone();
    }

    private static string Fact(string value)
    {
        return $$"""{"value": {{value}}, "source": null, "owner": null, "validFrom": null, "validUntil": null, "lastVerified": null}""";
    }

    // Each read's progress goes into seen, when given.
    private static async Task<JsonObject> FinishedAsync(ConnectedAgent agent, string jobId, List<int>? seen = null)
    {
        // The job takes a few seconds for 180 items on its own; a loaded test machine can take far longer.
        var deadline = DateTime.UtcNow.AddMinutes(3);
        while (true)
        {
            var job = await agent.CallAsync("get_job", new() { ["id"] = jobId });
            seen?.Add((int)job["progress"]!["done"]!);
            if ((string?)job["state"] is "succeeded" or "failed" or "dead" || DateTime.UtcNow > deadline)
            {
                return job;
            }

            await Task.Delay(100);
        }
    }

    // The product's bulk handler with a test's step around each attempt, run by the real worker. It is registered before the
    // product's handlers, and the worker takes the first handler of a job's type.
    private sealed class SteppedBulkUpdates(Func<JobContext, Func<JobContext, Task<Result>>, Task<Result>> attempt) : IJobHandler
    {
        public string Type => BulkEntryUpdates.Name;

        public Task<Result> RunAsync(JobContext context, CancellationToken cancellationToken)
        {
            return attempt(context, inner => new BulkEntryUpdates().RunAsync(inner, cancellationToken));
        }

        public void Register(WebApplicationBuilder builder)
        {
            builder.Services.AddSingleton<IJobHandler>(this);
        }
    }
}
