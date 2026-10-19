using System.Text.Json.Nodes;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Results;
using FluentCMS.Core.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Builtin.Tools;

// An agent's change sets (CHG-01, CHG-03, CHG-04): reading a set and its diff, opening one, and submitting or reopening it.
// Approving, rejecting and publishing are a person's, so no tool does them (MCP-06).
public sealed class ChangeSetTools : IToolProvider
{
    private const string FindIds = "A change set's id comes back from the call that opened it or added an operation to it.";

    public IReadOnlyList<AgentTool> Tools { get; } =
    [
        new AgentTool(
            "get_change_set",
            "Get a change set",
            "Returns one change set: its state, rationale, sources, who opened it and its operations in order, each naming the "
            + "item, the version it was based on and the version it proposes. States run open, checks_running, then checks_failed "
            + "or awaiting_review, approved, published; a set can also be rejected (with a reason) or reverted. To see what the "
            + "operations change, call get_change_set_diff.",
            ToolSchemas.Object(ToolSchemas.Uuid("id", "The change set's id.")),
            readOnly: true,
            GetAsync),
        new AgentTool(
            "get_change_set_diff",
            "Get a change set's diff",
            "Returns what each operation of a change set changes, in operation order: the item's properties and fields that were "
            + "added, removed or changed, each with its value before and after. Unchanged values are left out, so an empty list "
            + "means the operation changes nothing there.",
            ToolSchemas.Object(ToolSchemas.Uuid("id", "The change set's id.")),
            readOnly: true,
            DiffAsync),
        new AgentTool(
            "open_change_set",
            "Open a change set",
            "Opens a change set for the changes you are about to propose, with the reason for them and where they come from. "
            + "Pass its id as change_set_id to the write tools, then call submit_change_set. A person reviews the set before "
            + "anything in it is published. To group operations without keeping an id, give the write tools a session_id instead.",
            ToolSchemas.Object(
                ToolSchemas.String("rationale", "Why the changes are made, for the person who reviews them.", "Add a price field so product pages can show prices."),
                ToolSchemas.Strings("sources", "Where the changes come from, such as URLs or file names.", "https://example.com/catalog-spec", required: false)),
            readOnly: false,
            OpenAsync),
        new AgentTool(
            "submit_change_set",
            "Submit a change set for checks and review",
            "Submits one of your open change sets once it holds every operation it needs: the site's checks run on it, and a person "
            + "reviews it before anything is published. A small set is checked at once and the answer holds its check results; a "
            + "larger one, or one an LLM-judged check reads, is checked in the background, so follow it with get_change_set_checks. "
            + "A submitted set takes no more operations. An agent never approves or publishes a set; if checks fail, reopen it with "
            + "reopen_change_set, fix what they found and submit it again.",
            ToolSchemas.Object(ToolSchemas.Uuid("id", "The id of your open change set.")),
            readOnly: false,
            SubmitAsync),
        new AgentTool(
            "reopen_change_set",
            "Reopen a change set after failed checks",
            "Reopens one of your change sets whose checks failed, so you can add operations that fix it and submit it again. "
            + "Read the set with get_change_set first.",
            ToolSchemas.Object(ToolSchemas.Uuid("id", "The id of your change set whose checks failed.")),
            readOnly: false,
            (call, cancellationToken) => MoveAsync(call, ChangeSetState.Open, cancellationToken)),
    ];

    private static async Task<ToolResult> OpenAsync(ToolInvocation call, CancellationToken cancellationToken)
    {
        var arguments = new ToolArgumentsReader(call.Arguments);
        var rationale = arguments.RequiredString("rationale", "Add a price field so product pages can show prices.");
        var sources = arguments.OptionalStrings("sources", "https://example.com/catalog-spec");
        if (arguments.Errors() is { Count: > 0 } errors)
        {
            return ToolResult.Failure(errors);
        }

        var set = await call.Services.GetRequiredService<IChangeSetDirectory>().OpenAsync(new NewChangeSet(rationale!, sources ?? []), cancellationToken);
        return ToolResult.From(set, opened => new JsonObject
        {
            ["changeSet"] = ToolJson.ChangeSet(opened),
            ["next"] = $"Pass change_set_id {opened.Id} to create_content_type or revise_content_type, then call submit_change_set.",
        });
    }

    private static async Task<ToolResult> SubmitAsync(ToolInvocation call, CancellationToken cancellationToken)
    {
        var arguments = new ToolArgumentsReader(call.Arguments);
        var id = arguments.RequiredGuid("id");
        if (arguments.Errors() is { Count: > 0 } errors)
        {
            return ToolResult.Failure(errors);
        }

        var submitted = await call.Services.GetRequiredService<IChangeSetChecks>().SubmitAsync(id!.Value, cancellationToken);
        if (!submitted.IsSuccess)
        {
            return ToolResult.Failure(submitted.Errors, Hint(Result<ChangeSet>.Failure(submitted.Errors)));
        }

        var submission = submitted.Value;
        var set = submission.ChangeSet;
        return ToolResult.Success(new JsonObject
        {
            ["changeSet"] = ToolJson.ChangeSet(set),
            ["checks"] = submission.Run is { } run ? ToolJson.CheckRun(run) : null,
            ["job"] = submission.Run is null ? ToolJson.Job(submission.Job) : null,
            ["next"] = set.State switch
            {
                ChangeSetState.ChecksFailed =>
                    "Checks failed: each failing result says what to change. Call reopen_change_set, fix the items and submit the set again.",
                ChangeSetState.AwaitingReview => "Checks passed, and a person reviews the set next. Follow its state with get_change_set.",
                _ => $"Checks run in the background as job {submission.Job.Id}. Follow them with get_change_set_checks.",
            },
        });
    }

    private static async Task<ToolResult> MoveAsync(ToolInvocation call, ChangeSetState target, CancellationToken cancellationToken)
    {
        var arguments = new ToolArgumentsReader(call.Arguments);
        var id = arguments.RequiredGuid("id");
        if (arguments.Errors() is { Count: > 0 } errors)
        {
            return ToolResult.Failure(errors);
        }

        var set = await call.Services.GetRequiredService<IChangeSetDirectory>().TransitionAsync(id!.Value, target, cancellationToken);
        return ToolResult.From(
            set,
            moved => new JsonObject
            {
                ["changeSet"] = ToolJson.ChangeSet(moved),
                ["next"] = target == ChangeSetState.Open
                    ? "The set is open again: add the operations that fix it, then call submit_change_set."
                    : "Checks run next, and a person reviews the set. Follow its state with get_change_set.",
            },
            Hint(set));
    }

    private static async Task<ToolResult> GetAsync(ToolInvocation call, CancellationToken cancellationToken)
    {
        var arguments = new ToolArgumentsReader(call.Arguments);
        var id = arguments.RequiredGuid("id");
        if (arguments.Errors() is { Count: > 0 } errors)
        {
            return ToolResult.Failure(errors);
        }

        var set = await call.Services.GetRequiredService<IChangeSetDirectory>().GetAsync(id!.Value, cancellationToken);
        return ToolResult.From(set, ToolJson.ChangeSet, Hint(set));
    }

    private static async Task<ToolResult> DiffAsync(ToolInvocation call, CancellationToken cancellationToken)
    {
        var arguments = new ToolArgumentsReader(call.Arguments);
        var id = arguments.RequiredGuid("id");
        if (arguments.Errors() is { Count: > 0 } errors)
        {
            return ToolResult.Failure(errors);
        }

        var diff = await call.Services.GetRequiredService<IChangeSetDirectory>().DiffAsync(id!.Value, cancellationToken);
        return ToolResult.From(diff, ToolJson.Diff, Hint(diff));
    }

    private static string? Hint<T>(Result<T> result)
    {
        return result.Errors.FirstOrDefault()?.Code == ChangeSetErrorCodes.ChangeSetNotFound ? FindIds : null;
    }
}
