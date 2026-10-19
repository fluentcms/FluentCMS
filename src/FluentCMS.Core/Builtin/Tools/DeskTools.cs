using System.Text.Json.Nodes;
using FluentCMS.Core.Audit;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Desk;
using FluentCMS.Core.Identity;
using FluentCMS.Core.Results;
using FluentCMS.Core.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Builtin.Tools;

// The review desk for agents, read only (MCP-01, DSK): what waits in each queue and where a change set stands in review. Review
// decisions are a person's, so no tool makes one (MCP-06).
public sealed class DeskTools : IToolProvider
{
    private const int DefaultLimit = 50;

    // Awaiting me is a person's queue, so an agent reads the sets awaiting review as awaiting anyone.
    private static readonly Dictionary<string, DeskQueue> Queues = new(StringComparer.Ordinal)
    {
        ["awaiting_review"] = DeskQueue.AwaitingAnyone,
        ["checks_failed"] = DeskQueue.ChecksFailed,
        ["stale_facts"] = DeskQueue.StaleFacts,
        ["recently_published"] = DeskQueue.RecentlyPublished,
        ["reverted"] = DeskQueue.Reverted,
    };

    public IReadOnlyList<AgentTool> Tools { get; } =
    [
        new AgentTool(
            "get_desk_counts",
            "Get the review desk's counts",
            "Returns how many items wait in each queue of the review desk: sets awaiting review, sets whose checks failed, stale facts, "
            + "and sets published or reverted in the last seven days. Call list_desk_queue to see the items of one queue.",
            ToolSchemas.Object(),
            readOnly: true,
            CountsAsync),
        new AgentTool(
            "list_desk_queue",
            "List a review desk queue",
            "Lists one queue of the review desk, as reviewers see it: awaiting_review and checks_failed put the set that has waited "
            + "longest first; recently_published and reverted cover the last seven days, newest first; stale_facts lists the facts "
            + "that are stale, the longest stale first. Read a set with get_change_set or get_change_set_review.",
            ToolSchemas.Object(
                new ToolArgument(
                    "queue",
                    "The queue to list.",
                    true,
                    new JsonObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JsonArray([.. Queues.Keys.Select(name => JsonValue.Create(name))]),
                        ["examples"] = new JsonArray("awaiting_review"),
                    }),
                ToolSchemas.PositiveInteger("page", "The page to read, from 1.", 1, required: false),
                ToolSchemas.PositiveInteger("limit", $"How many items a page holds, at most {ChangeSetListQuery.MaxLimit}; {DefaultLimit} when left out.", DefaultLimit, required: false)),
            readOnly: true,
            ListAsync),
        new AgentTool(
            "get_change_set_review",
            "Get a change set's review status",
            "Returns where a change set stands in review: its state and since when, the approvals it has and how many it needs, the "
            + "reason if a person rejected it, the set it reverts, and every move it made, in order, with who made it and when. Use it to learn whether your set was approved, "
            + "published or rejected and why; a rejected set's reason says what to change in a new set.",
            ToolSchemas.Object(ToolSchemas.Uuid("id", "The change set's id.")),
            readOnly: true,
            ReviewAsync),
    ];

    private static async Task<ToolResult> CountsAsync(ToolInvocation call, CancellationToken cancellationToken)
    {
        var arguments = new ToolArgumentsReader(call.Arguments);
        if (arguments.Errors() is { Count: > 0 } errors)
        {
            return ToolResult.Failure(errors);
        }

        var counts = await call.Services.GetRequiredService<IDeskQueues>().CountAsync(cancellationToken);
        return ToolResult.From(counts, values => new JsonObject
        {
            ["counts"] = new JsonObject([.. Queues.Select(queue => KeyValuePair.Create(queue.Key, (JsonNode?)values[queue.Value]))]),
        });
    }

    private static async Task<ToolResult> ListAsync(ToolInvocation call, CancellationToken cancellationToken)
    {
        var arguments = new ToolArgumentsReader(call.Arguments);
        var name = arguments.RequiredString("queue", "awaiting_review");
        var page = arguments.OptionalPositiveInteger("page") ?? 1;
        var limit = arguments.OptionalPositiveInteger("limit") ?? DefaultLimit;
        var errors = arguments.Errors().ToList();
        if (name is not null && !Queues.ContainsKey(name))
        {
            errors.Add(new Error(ToolErrorCodes.ArgumentInvalid, $"The argument 'queue' must be one of {string.Join(", ", Queues.Keys)}, and it is '{name}'."));
        }

        if (limit > ChangeSetListQuery.MaxLimit)
        {
            errors.Add(new Error(ToolErrorCodes.ArgumentInvalid, $"The argument 'limit' must be at most {ChangeSetListQuery.MaxLimit}, and it is {limit}."));
        }

        if (errors.Count > 0)
        {
            return ToolResult.Failure(errors);
        }

        var queue = Queues[name!];
        var offset = (int)Math.Min((page - 1L) * limit, int.MaxValue - limit);
        var queues = call.Services.GetRequiredService<IDeskQueues>();

        if (queue == DeskQueue.StaleFacts)
        {
            var facts = await queues.ListStaleFactsAsync(offset, limit, cancellationToken);
            return ToolResult.From(facts, list => new JsonObject
            {
                ["queue"] = name,
                ["total"] = list.Total,
                ["facts"] = new JsonArray([.. list.Items.Select(ToolJson.Stale)]),
                ["next"] = "Re-verify each fact with set_entity_fact and a new last_verified, or a new value and valid_until.",
            });
        }

        var sets = await queues.ListChangeSetsAsync(queue, offset, limit, cancellationToken);
        return ToolResult.From(sets, list => new JsonObject
        {
            ["queue"] = name,
            ["total"] = list.Total,
            ["changeSets"] = new JsonArray([.. list.Items.Select(Summary)]),
            ["next"] = "Read a set's operations with get_change_set, its changes with get_change_set_diff and its review with get_change_set_review.",
        });
    }

    private static async Task<ToolResult> ReviewAsync(ToolInvocation call, CancellationToken cancellationToken)
    {
        var arguments = new ToolArgumentsReader(call.Arguments);
        var id = arguments.RequiredGuid("id");
        if (arguments.Errors() is { Count: > 0 } errors)
        {
            return ToolResult.Failure(errors);
        }

        var read = await call.Services.GetRequiredService<IChangeSetDirectory>().GetAsync(id!.Value, cancellationToken);
        if (!read.IsSuccess)
        {
            return ToolResult.Failure(read.Errors, "A change set's id comes back from the call that opened it, or from list_desk_queue.");
        }

        var set = read.Value;
        var history = call.Services.GetRequiredService<IAuditReader>().Read(new AuditQuery { Subject = new AuditSubject("change_set", set.Id) });
        var moves = new JsonArray();
        DateTimeOffset? since = null;
        if (history.IsSuccess)
        {
            await foreach (var auditEvent in history.Value.WithCancellation(cancellationToken))
            {
                if (!ChangeSetMoves.Operations.Contains(auditEvent.Operation))
                {
                    continue;
                }

                since = auditEvent.Timestamp;
                moves.Add(new JsonObject
                {
                    ["move"] = auditEvent.Operation["change_set.".Length..],
                    ["by"] = Initiator(auditEvent.Initiator),
                    ["at"] = auditEvent.Timestamp.ToUniversalTime().ToString("O"),
                });
            }
        }

        return ToolResult.Success(new JsonObject
        {
            ["id"] = set.Id.ToString(),
            ["state"] = ToolJson.Name(set.State),
            ["stateSince"] = since?.ToUniversalTime().ToString("O"),
            ["rejectionReason"] = set.RejectionReason,
            ["revertsChangeSetId"] = set.RevertsChangeSetId?.ToString(),
            ["requiredApprovals"] = set.RequiredApprovals,
            ["approvals"] = new JsonArray([.. set.Approvals.Select(approval => (JsonNode)new JsonObject
            {
                ["by"] = Initiator(approval.Approver),
                ["at"] = approval.ApprovedAt.ToUniversalTime().ToString("O"),
            })]),
            ["moves"] = moves,
        });
    }

    private static JsonObject Summary(ChangeSetSummary set)
    {
        return new JsonObject
        {
            ["id"] = set.Id.ToString(),
            ["state"] = ToolJson.Name(set.State),
            ["rationale"] = set.Rationale,
            ["initiator"] = Initiator(set.Initiator),
            ["items"] = set.ItemCount,
            ["revertsChangeSetId"] = set.RevertsChangeSetId?.ToString(),
            ["openedAt"] = set.OpenedAt.ToUniversalTime().ToString("O"),
            ["stateSince"] = set.StateSince.ToUniversalTime().ToString("O"),
        };
    }

    private static JsonObject Initiator(Initiator initiator)
    {
        var kind = initiator switch
        {
            AgentInitiator => "agent",
            SystemInitiator => "system",
            _ => "user",
        };

        return new JsonObject { ["kind"] = kind, ["name"] = initiator.DisplayName };
    }
}
