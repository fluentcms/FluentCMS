using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Results;
using FluentCMS.Core.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Builtin.Tools;

// Reading change sets (CHG-01, CHG-03): a set's state and operations, and what each operation changes field by field.
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
    ];

    private static async Task<ToolResult> GetAsync(ToolCall call, CancellationToken cancellationToken)
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

    private static async Task<ToolResult> DiffAsync(ToolCall call, CancellationToken cancellationToken)
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
