using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.Checks;
using FluentCMS.Core.Content;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Builtin.Tools;

// Checks (CHK-04): an agent runs the active checks on a draft before it proposes the draft, so it can fix what they find without
// opening a change set or waiting for a review.
public sealed class CheckTools : IToolProvider
{
    private const string FindContentTypes = "Call list_content_types for the slugs of the site's content types.";
    private const string FindEntries = "Call list_entries with the content type's slug for the ids of its entries.";

    public IReadOnlyList<AgentTool> Tools { get; } =
    [
        new AgentTool(
            "check_draft",
            "Check a draft",
            "Runs the site's active checks on a draft of an entry without storing it or opening a change set, and returns each check's "
            + "status (pass, warn, fail or needs_review) with findings that say what to change and where, plus any field the content "
            + "type refuses. Call it before create_entry, or before update_entry with the entry's id so checks see what changed; fix "
            + "what it finds and call it again. LLM-judged checks ask a model and count against your model spend; give checks "
            + "\"deterministic\" to run only the others, and the ones left out are listed under skipped.",
            ToolSchemas.Object(
                ToolSchemas.String("content_type", "The slug of the draft's content type, as list_content_types shows it.", "product-page"),
                ToolSchemas.Json(
                    "fields",
                    "The draft's fields, one property per field of the content type, as create_entry takes them.",
                    new JsonObject { ["type"] = "object" },
                    JsonNode.Parse("""{"headline": "A kettle that boils in two minutes", "body": "Made of steel."}""")!),
                ToolSchemas.Strings(
                    "sources",
                    "Where the draft's content came from, such as URLs, files or exports, as the change set will cite them.",
                    "exports/catalog-2026-10-18.csv",
                    required: false),
                ToolSchemas.String("checks", "Which checks to run: \"all\", the default, or \"deterministic\".", "deterministic", required: false),
                ToolSchemas.Uuid("id", "For a draft of an update: the entry it updates, so checks that ask about changes see only what differs.", required: false),
                ToolSchemas.Uuid(
                    "change_set_id",
                    "The change set the draft will go into, so checks read the media versions it proposes, such as alt text added with update_media.",
                    required: false)),

            // Not read-only: it stores nothing, but an LLM-judged check spends the agent's model budget.
            readOnly: false,
            CheckDraftAsync),
        new AgentTool(
            "get_change_set_checks",
            "Get a change set's check results",
            "Returns the latest check run of a change set (CHK-03): its status, cost and each item's results, with findings that say "
            + "what to change and where, plus the background job that runs the checks with its progress. A set's checks run when it "
            + "is submitted; if they failed, call reopen_change_set, fix the items and submit the set again.",
            ToolSchemas.Object(ToolSchemas.Uuid("id", "The change set's id.")),
            readOnly: true,
            ChangeSetChecksAsync),
    ];

    private static async Task<ToolResult> CheckDraftAsync(ToolInvocation call, CancellationToken cancellationToken)
    {
        var arguments = new ToolArgumentsReader(call.Arguments);
        var slug = arguments.RequiredString("content_type", "product-page");
        var fields = arguments.RequiredObject("fields", "an object with one property per field");
        var sources = arguments.OptionalStrings("sources", "exports/catalog-2026-10-18.csv") ?? [];
        var checks = arguments.OptionalString("checks", "deterministic");
        var id = arguments.OptionalGuid("id");
        var changeSetId = arguments.OptionalGuid("change_set_id");
        var errors = arguments.Errors().ToList();
        if (checks is not (null or "all" or "deterministic"))
        {
            errors.Add(new Error(ToolErrorCodes.ArgumentInvalid, "The argument 'checks' must be \"all\" or \"deterministic\"."));
        }

        if (errors.Count > 0)
        {
            return ToolResult.Failure(errors);
        }

        var services = call.Services;
        var type = await services.GetRequiredService<IContentTypeDirectory>().GetAsync(slug!, cancellationToken);
        if (!type.IsSuccess)
        {
            return ToolResult.Failure(type.Errors, type.Errors.Any(error => error.Code == SchemaErrorCodes.ContentTypeNotFound) ? FindContentTypes : null);
        }

        IReadOnlyList<string>? changed = null;
        if (id is { } entryId)
        {
            var entry = await services.GetRequiredService<IEntryDirectory>().GetAsync(entryId, cancellationToken);
            if (!entry.IsSuccess)
            {
                return ToolResult.Failure(entry.Errors, entry.Errors.Any(error => error.Code == ContentErrorCodes.EntryNotFound) ? FindEntries : null);
            }

            if (entry.Value.ContentType != slug)
            {
                return ToolResult.Failure(
                    [new Error(ToolErrorCodes.ArgumentInvalid, $"The entry {entryId} is of the content type '{entry.Value.ContentType}', not '{slug}'.")],
                    FindEntries);
            }

            changed = Changed(fields!.Value, entry.Value.Latest.Fields);
        }

        var definition = type.Value.Definition;
        var refused = EntryFields.Validate(fields!.Value, definition, services.GetRequiredService<FieldTypeRegistry>()).Errors;
        var run = await services.GetRequiredService<ICheckRunner>().RunAsync(
            new CheckSubject(fields.Value, sources, slug, definition, changed, changeSetId),
            checks == "deterministic" ? CheckSelection.Deterministic : CheckSelection.All,
            cancellationToken: cancellationToken);

        // A draft the content type refuses cannot be proposed, whatever the checks say.
        return ToolResult.Success(new JsonObject
        {
            ["status"] = ToolJson.Name(refused.Count > 0 ? CheckStatus.Fail : CheckResult.Overall(run.Results)),
            ["results"] = new JsonArray([.. run.Results.Select(ToolJson.CheckResult)]),
            ["skipped"] = new JsonArray([.. run.Skipped.Select(name => JsonValue.Create(name))]),
            ["fieldErrors"] = new JsonArray([.. refused.Select(error => new JsonObject { ["code"] = error.Code, ["message"] = error.Message })]),
        });
    }

    // The fields whose value the draft sets to something the latest version does not hold.
    private static List<string> Changed(JsonElement draft, JsonElement latest)
    {
        return draft.EnumerateObject()
            .Where(field => latest.ValueKind != JsonValueKind.Object
                || !latest.TryGetProperty(field.Name, out var before)
                || !JsonElement.DeepEquals(before, field.Value))
            .Select(field => field.Name)
            .ToList();
    }

    private static async Task<ToolResult> ChangeSetChecksAsync(ToolInvocation call, CancellationToken cancellationToken)
    {
        var arguments = new ToolArgumentsReader(call.Arguments);
        var id = arguments.RequiredGuid("id");
        if (arguments.Errors() is { Count: > 0 } errors)
        {
            return ToolResult.Failure(errors);
        }

        var state = await call.Services.GetRequiredService<IChangeSetChecks>().GetAsync(id!.Value, cancellationToken);
        return ToolResult.From(state, read => new JsonObject
        {
            ["changeSet"] = ToolJson.ChangeSet(read.ChangeSet),
            ["checks"] = read.Run is { } run ? ToolJson.CheckRun(run) : null,
            ["job"] = read.Job is { } job ? ToolJson.Job(job) : null,
        });
    }
}
