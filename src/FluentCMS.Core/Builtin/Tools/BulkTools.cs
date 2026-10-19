using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.Agents;
using FluentCMS.Core.Builtin.Jobs;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Jobs;
using FluentCMS.Core.Persistence;
using FluentCMS.Core.Results;
using FluentCMS.Core.Sites;
using FluentCMS.Core.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Builtin.Tools;

// Bulk writes (MCP-08): the call opens a change set and enqueues the work in one unit of work, and returns both ids at once; the
// job applies the items in the background, and get_job shows how far it got.
public sealed class BulkTools : IToolProvider
{
    public const int MaxItems = 1000;

    private const string ItemsForm = "an array of {\"id\", \"base_version\", \"fields\"} objects, one per entry or entity";

    public IReadOnlyList<AgentTool> Tools { get; } =
    [
        new AgentTool(
            "bulk_update_entries",
            "Propose updates of many entries at once",
            "Proposes new fields for many entries or entities in one call: it opens a change set for them and returns its id and a "
            + "job id at once, and the updates are applied in the background, each as update_entry would. Follow the job with "
            + "get_job; an item that fails, such as one updated since you read it, is listed there and the rest go on. Once the "
            + "job has succeeded, submit the set with submit_change_set. The list may not be longer than your items-per-change-set cap.",
            ToolSchemas.Object(
                ToolSchemas.Json(
                    "items",
                    $"The updates, at most {MaxItems}: each names an entry or entity, the latest version you read and its whole new fields.",
                    new JsonObject
                    {
                        ["type"] = "array",
                        ["minItems"] = 1,
                        ["maxItems"] = MaxItems,
                        ["items"] = new JsonObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JsonObject
                            {
                                ["id"] = new JsonObject { ["type"] = "string", ["format"] = "uuid" },
                                ["base_version"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1 },
                                ["fields"] = new JsonObject { ["type"] = "object" },
                            },
                            ["required"] = new JsonArray("id", "base_version", "fields"),
                            ["additionalProperties"] = false,
                        },
                    },
                    JsonNode.Parse("""[{"id": "0192f3a1-7c2e-7a4b-9d3e-1f2a3b4c5d6e", "base_version": 2, "fields": {"title": "Widget", "price": 19.99}}]""")!),
                ToolSchemas.String("rationale", "Why the updates are made, for the person who reviews them.", "Sync prices from the catalog export."),
                ToolSchemas.Strings("sources", "Where the updates come from, such as URLs or file names.", "exports/catalog-2026-10-19.csv", required: false)),
            readOnly: false,
            UpdateAsync),
    ];

    private static async Task<ToolResult> UpdateAsync(ToolInvocation call, CancellationToken cancellationToken)
    {
        var arguments = new ToolArgumentsReader(call.Arguments);
        var list = arguments.RequiredArray("items", ItemsForm);
        var rationale = arguments.RequiredString("rationale", "Sync prices from the catalog export.");
        var sources = arguments.OptionalStrings("sources", "exports/catalog-2026-10-19.csv") ?? [];
        var errors = new List<Error>(arguments.Errors());
        var items = list is { } elements ? ReadItems(elements, errors) : [];
        if (errors.Count > 0)
        {
            return ToolResult.Failure(errors);
        }

        var services = call.Services;
        var initiator = services.GetRequiredService<ICurrentInitiator>().Current
            ?? throw new InvalidOperationException("No initiator is known for this call; a change set records who opened it (CHG-01).");

        // Refused ahead of any write, so a refused cap keeps its security event (SEC-08) and no set is opened for nothing.
        var planned = ChangeSet.Open(Site.Default.Id, rationale!, sources, initiator);
        if (!planned.IsSuccess)
        {
            return ToolResult.Failure(planned.Errors);
        }

        var permissions = services.GetRequiredService<IAgentPermissions>();
        foreach (var check in new Func<Task<Result>>[]
        {
            () => permissions.CheckNewChangeSetAsync(cancellationToken),
            () => permissions.CheckChangeSetSizeAsync(planned.Value, adding: items.Count, cancellationToken),
        })
        {
            var allowed = await check();
            if (!allowed.IsSuccess)
            {
                return ToolResult.Failure(allowed.Errors);
            }
        }

        // The set and the job commit together, so there is never a job without its set or a set nobody is working on.
        await using var scope = await services.GetRequiredService<IUnitOfWork>().BeginAsync(cancellationToken);

        var set = await services.GetRequiredService<IChangeSetDirectory>().OpenAsync(new NewChangeSet(rationale!, sources), cancellationToken);
        if (!set.IsSuccess)
        {
            return ToolResult.Failure(set.Errors);
        }

        var job = await services.GetRequiredService<IJobQueue>().EnqueueAsync(
            new NewJob(BulkEntryUpdates.Name, BulkEntryUpdates.Payload(set.Value.Id, items)) { Subject = new JobSubject("change_set", set.Value.Id) },
            cancellationToken);

        await scope.CommitAsync(cancellationToken);

        return ToolResult.Success(new JsonObject
        {
            ["changeSet"] = ToolJson.ChangeSet(set.Value),
            ["job"] = ToolJson.Job(job),
            ["next"] = $"The {items.Count} updates are applied in the background. Call get_job with id {job.Id} until it has succeeded, "
                + $"then submit_change_set with id {set.Value.Id}.",
        });
    }

    // Every malformed item is reported at once by its index, before anything is opened.
    private static List<BulkEntryUpdate> ReadItems(JsonElement list, List<Error> errors)
    {
        var items = new List<BulkEntryUpdate>();
        var seen = new HashSet<Guid>();
        var index = 0;

        foreach (var element in list.EnumerateArray())
        {
            var reader = new ToolArgumentsReader(element);
            var id = reader.RequiredGuid("id");
            var baseVersion = reader.RequiredPositiveInteger("base_version");
            var fields = reader.RequiredObject("fields", "an object with the entry's fields");
            var itemErrors = reader.Errors();

            errors.AddRange(itemErrors.Select(error => new Error(error.Code, $"items[{index}]: {error.Message}")));
            if (itemErrors.Count == 0)
            {
                if (seen.Add(id!.Value))
                {
                    items.Add(new BulkEntryUpdate(id.Value, baseVersion!.Value, fields!.Value.Clone()));
                }
                else
                {
                    errors.Add(new Error(ToolErrorCodes.ArgumentInvalid, $"items[{index}]: {id} is already in the list; give each entry once."));
                }
            }

            index++;
        }

        if (index == 0)
        {
            errors.Add(new Error(ToolErrorCodes.ArgumentInvalid, "'items' is empty: give at least one update."));
        }
        else if (index > MaxItems)
        {
            errors.Add(new Error(ToolErrorCodes.ArgumentInvalid, $"'items' has {index} updates and a call takes at most {MaxItems}; split them over several calls."));
        }

        return items;
    }
}
