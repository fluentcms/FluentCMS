using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.Agents;
using FluentCMS.Core.Builtin.Jobs;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Content;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Tools;
using FluentCMS.Infrastructure.Persistence;
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
        var finished = await FinishedAsync(agent, jobId);

        Assert.Equal("open", (string?)started["changeSet"]!["state"]);
        Assert.Equal(changeSetId, (string?)started["job"]!["subject"]!["id"]);
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
        var products = await CreateProductsAsync(agent, 3);

        var started = await agent.CallAsync("bulk_update_entries", new()
        {
            ["items"] = new[] { Item(products[0], 1, 1m), Item(products[1], 5, 2m), Item(products[2], 1, 3m) },
            ["rationale"] = "Sync prices.",
        });
        var finished = await FinishedAsync(agent, (string)started["job"]!["id"]!);

        Assert.Equal("failed", (string?)finished["state"]);
        Assert.Equal(1, (int)finished["attempts"]!);
        Assert.Contains($"items[1] {products[1]}", (string?)finished["lastError"]);
        Assert.Equal("Updated 2 of 3; 1 failed.", (string?)finished["progress"]!["message"]);
        var set = await agent.AsPersonAsync<IChangeSetDirectory, ChangeSet>(directory => directory.GetAsync(Guid.Parse((string)started["changeSet"]!["id"]!)));
        Assert.Equal([products[0], products[2]], set.Operations.Select(operation => operation.ItemId));
    }

    [Fact]
    public async Task A_list_past_the_cap_or_with_a_malformed_item_is_refused_before_a_set_is_opened()
    {
        await using var agent = await ConnectedAgent.StartAsync(DatabaseProviders.Sqlite, itemsPerChangeSet: 2, settings: FastQueue);
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
        Assert.Equal(
            [(ToolErrorCodes.ArgumentInvalid, "items[1]"), (ToolErrorCodes.ArgumentMissing, "items[1]"), (ToolErrorCodes.ArgumentInvalid, "items[2]")],
            malformed["errors"]!.AsArray().Select(error => ((string)error!["code"]!, ((string)error["message"]!).Split(':')[0])));
        Assert.Empty(await agent.QueryAsync(context => context.ChangeSets.AsNoTracking().ToListAsync()));
        Assert.Empty(await agent.QueryAsync(context => context.Jobs.AsNoTracking().ToListAsync()));
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

    [Fact]
    public async Task Get_job_shows_an_agent_its_own_jobs_only()
    {
        await using var agent = await ConnectedAgent.StartAsync(DatabaseProviders.Sqlite, settings: FastQueue);
        var products = await CreateProductsAsync(agent, 1);
        var started = await agent.CallAsync("bulk_update_entries", new() { ["items"] = new[] { Item(products[0], 1, 1m) }, ["rationale"] = "One price." });
        var someoneElses = await agent.AsPersonAsync<IServiceProvider, Job>(async services =>
        {
            await using var work = await services.GetRequiredService<Core.Persistence.IUnitOfWork>().BeginAsync();
            var job = await services.GetRequiredService<IJobQueue>().EnqueueAsync(new NewJob("test.someone-else", default) { RunAt = DateTimeOffset.UtcNow.AddDays(1) });
            await work.CommitAsync();
            return Core.Results.Result<Job>.Success(job);
        });

        var own = await agent.CallAsync("get_job", new() { ["id"] = (string)started["job"]!["id"]! });
        var other = await agent.FailAsync("get_job", new() { ["id"] = someoneElses.Id.ToString() });

        Assert.Equal(BulkEntryUpdates.Name, (string?)own["type"]);
        Assert.Equal(JobErrorCodes.JobNotFound, (string?)other["errors"]![0]!["code"]);
    }

    private static async Task<List<Guid>> CreateProductsAsync(ConnectedAgent agent, int count)
    {
        await agent.AsPersonAsync<IContentTypeDirectory, ContentType>(directory => directory.CreateAsync(new NewContentType(
            "Product",
            "product",
            new ContentTypeDefinition([new FieldDefinition("name", "text"), new FieldDefinition("price", "number")], isEntity: true))));

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

    private static async Task<JsonObject> FinishedAsync(ConnectedAgent agent, string jobId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (true)
        {
            var job = await agent.CallAsync("get_job", new() { ["id"] = jobId });
            if ((string?)job["state"] is "succeeded" or "failed" or "dead" || DateTime.UtcNow > deadline)
            {
                return job;
            }

            await Task.Delay(100);
        }
    }
}
