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

    public IReadOnlyList<AgentTool> Tools { get; } =
    [
        new AgentTool(
            "check_draft",
            "Check a draft",
            "Runs the site's active checks on a draft of an entry without storing it or opening a change set, and returns each check's "
            + "status (pass, warn, fail or needs_review) with findings that say what to change and where, plus any field the content "
            + "type refuses. Call it before create_entry or update_entry, fix what it finds and call it again. LLM-judged checks ask a "
            + "model and count against your model spend; give checks \"deterministic\" to run only the others.",
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
                ToolSchemas.String("checks", "Which checks to run: \"all\", the default, or \"deterministic\".", "deterministic", required: false)),
            readOnly: true,
            CheckDraftAsync),
    ];

    private static async Task<ToolResult> CheckDraftAsync(ToolInvocation call, CancellationToken cancellationToken)
    {
        var arguments = new ToolArgumentsReader(call.Arguments);
        var slug = arguments.RequiredString("content_type", "product-page");
        var fields = arguments.RequiredObject("fields", "an object with one property per field");
        var sources = arguments.OptionalStrings("sources", "exports/catalog-2026-10-18.csv") ?? [];
        var checks = arguments.OptionalString("checks", "deterministic");
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
            return ToolResult.Failure(type.Errors, FindContentTypes);
        }

        var definition = type.Value.Definition;
        var refused = EntryFields.Validate(fields!.Value, definition, services.GetRequiredService<FieldTypeRegistry>()).Errors;
        var results = await services.GetRequiredService<ICheckRunner>().RunAsync(
            new CheckSubject(fields.Value, sources, slug, definition),
            checks == "deterministic" ? CheckSelection.Deterministic : CheckSelection.All,
            cancellationToken);

        return ToolResult.Success(new JsonObject
        {
            ["status"] = ToolJson.Name(CheckResult.Overall(results)),
            ["results"] = new JsonArray([.. results.Select(Result)]),
            ["fieldErrors"] = new JsonArray([.. refused.Select(error => new JsonObject { ["code"] = error.Code, ["message"] = error.Message })]),
        });
    }

    private static JsonObject Result(CheckResult result)
    {
        return new JsonObject
        {
            ["check"] = result.Check,
            ["version"] = result.Version,
            ["type"] = result.Type,
            ["kind"] = ToolJson.Name(result.Kind),
            ["severity"] = ToolJson.Name(result.Severity),
            ["description"] = result.Description,
            ["status"] = ToolJson.Name(result.Status),
            ["findings"] = new JsonArray([.. result.Findings.Select(finding => new JsonObject
            {
                ["message"] = finding.Message,
                ["pointer"] = finding.Pointer,
                ["excerpt"] = finding.Excerpt,
            })]),
            ["model"] = result.Model is { } model
                ? new JsonObject { ["callId"] = model.CallId.ToString(), ["provider"] = model.Provider, ["model"] = model.Model, ["cost"] = model.Cost }
                : null,
        };
    }
}
