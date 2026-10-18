using System.Text.Json.Nodes;
using FluentCMS.Core.ChangeSets;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Builtin.Tools;

// The schema (SCH-01, SCH-06, MCP-03): reading the content types and the schema document entries are written from, and proposing
// new types and revisions, which go into a change set a person reviews.
public sealed class SchemaTools : IToolProvider, IResourceProvider
{
    private const string FindSlugs = "Call list_content_types for the slugs of the site's content types.";
    private const string JsonMediaType = "application/schema+json";

    public IReadOnlyList<AgentTool> Tools { get; } =
    [
        new AgentTool(
            "list_content_types",
            "List content types",
            "Lists every content type of the site with its id, slug, name and current version. Call this first to find the slug "
            + "the other schema tools take; read one type's fields with get_content_type, or the schema to write entries against "
            + "with get_schema_document.",
            ToolSchemas.Object(),
            readOnly: true,
            ListAsync),
        new AgentTool(
            "get_content_type",
            "Get a content type",
            "Returns one content type with its fields: each field's name, type, whether it is required, its description and its "
            + "settings. Without a version it returns the current one; earlier versions stay readable. To write an entry, the "
            + "schema document from get_schema_document says more: every field's allowed values with an example.",
            ToolSchemas.Object(
                ToolSchemas.String("slug", "The content type's slug, as list_content_types shows it.", "product"),
                ToolSchemas.PositiveInteger("version", "A version to read instead of the current one.", 1, required: false)),
            readOnly: true,
            GetAsync),
        new AgentTool(
            "get_schema_document",
            "Get the schema document",
            "Returns the JSON Schema (draft 2020-12) that entries follow, with a description and an example for every field and "
            + "one example entry. With a slug it covers that content type at its current version; without one, every type of "
            + "the site under $defs, keyed by slug. On an entity type every field is a fact that carries its source, owner and validity.",
            ToolSchemas.Object(
                ToolSchemas.String("slug", "A content type's slug, to read only that type's schema.", "product", required: false)),
            readOnly: true,
            SchemaDocumentAsync),
        new AgentTool(
            "create_content_type",
            "Propose a new content type",
            "Proposes a new content type: it stores version 1, unpublished, and adds the create to a change set in the same "
            + "call. Nothing is live until a person approves and publishes the set. Every problem with the definition comes back "
            + "at once, by field. Field types: text, richText, number, boolean, dateTime, enum, list, json, media and reference; "
            + "get_schema_document shows how values of each look.",
            ToolSchemas.Object([
                ToolSchemas.String("name", "The name people see, such as 'Product page'.", "Product"),
                ToolSchemas.String("slug", "Lowercase words joined by hyphens; entries and references use it, so it never changes.", "product"),
                Definition,
                .. ChangeSetTarget.Arguments]),
            readOnly: false,
            CreateAsync),
        new AgentTool(
            "revise_content_type",
            "Propose a revision of a content type",
            "Proposes a new version of a content type from the version you read, given as base_version: it stores the next "
            + "version, unpublished, and adds the update to a change set in the same call. The definition replaces the whole "
            + "previous one, so list every field to keep. If someone revised the type since you read it, the call is refused; "
            + "read it again with get_content_type.",
            ToolSchemas.Object([
                ToolSchemas.String("slug", "The content type's slug.", "product"),
                ToolSchemas.PositiveInteger("base_version", "The version you read and revise, as get_content_type returned it.", 1),
                Definition,
                .. ChangeSetTarget.Arguments]),
            readOnly: false,
            ReviseAsync),
    ];

    public IReadOnlyList<AgentResource> Resources { get; } =
    [
        new AgentResource(
            "fluentcms://schema",
            "schema_document",
            "The schema document of the site: the JSON Schema that entries of every content type follow, under $defs keyed by slug.",
            JsonMediaType,
            async (read, cancellationToken) => Result<string>.Success(
                (await read.Services.GetRequiredService<ISchemaDocuments>().GetSiteAsync(cancellationToken)).ToJsonString())),
        new AgentResource(
            "fluentcms://schema/{slug}",
            "content_type_schema",
            "The JSON Schema that entries of one content type follow, at its current version, with a description and an example for every field.",
            JsonMediaType,
            async (read, cancellationToken) =>
            {
                var document = await read.Services.GetRequiredService<ISchemaDocuments>().GetTypeAsync(read.Parameters["slug"], cancellationToken);
                return document.IsSuccess ? Result<string>.Success(document.Value.ToJsonString()) : Result<string>.Failure(document.Errors);
            }),
    ];

    private static ToolArgument Definition { get; } = ToolSchemas.Json(
        "definition",
        "The fields of the type in order, each with a name, a field type, whether it is required, a description and the "
        + "type's settings; 'entity' makes it an entity type whose fields are facts with a source (SCH-03).",
        new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["entity"] = new JsonObject { ["type"] = "boolean" },
                ["description"] = new JsonObject { ["type"] = "string" },
                ["fields"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["name"] = new JsonObject { ["type"] = "string" },
                            ["type"] = new JsonObject { ["type"] = "string" },
                            ["required"] = new JsonObject { ["type"] = "boolean" },
                            ["description"] = new JsonObject { ["type"] = "string" },
                            ["settings"] = new JsonObject { ["type"] = "object" },
                        },
                        ["required"] = new JsonArray("name", "type"),
                    },
                },
            },
            ["required"] = new JsonArray("fields"),
        },
        JsonNode.Parse("""{"fields": [{"name": "title", "type": "text", "required": true}, {"name": "price", "type": "number", "settings": {"min": 0}}]}""")!);

    private static async Task<ToolResult> CreateAsync(ToolInvocation call, CancellationToken cancellationToken)
    {
        var arguments = new ToolArgumentsReader(call.Arguments);
        var name = arguments.RequiredString("name", "Product");
        var slug = arguments.RequiredString("slug", "product");
        var definition = arguments.RequiredObject("definition", DefinitionForm);
        var errors = new List<Error>();
        var target = ChangeSetTarget.Read(arguments, errors);
        if (Refused(arguments, errors, definition, out var parsed) is { } refused)
        {
            return refused;
        }

        return await ProposedWrite.RunAsync(
            call,
            target!,
            directory => directory.CreateAsync(new NewContentType(name!, slug!, parsed!), cancellationToken),
            type => ChangeOperation.Create(OperationKind.Create, ItemKind.Schema, type.Id, null, type.Version).Value,
            cancellationToken);
    }

    private static async Task<ToolResult> ReviseAsync(ToolInvocation call, CancellationToken cancellationToken)
    {
        var arguments = new ToolArgumentsReader(call.Arguments);
        var slug = arguments.RequiredString("slug", "product");
        var baseVersion = arguments.RequiredPositiveInteger("base_version");
        var definition = arguments.RequiredObject("definition", DefinitionForm);
        var errors = new List<Error>();
        var target = ChangeSetTarget.Read(arguments, errors);
        if (Refused(arguments, errors, definition, out var parsed) is { } refused)
        {
            return refused;
        }

        return await ProposedWrite.RunAsync(
            call,
            target!,
            directory => directory.ReviseAsync(slug!, baseVersion!.Value, parsed!, cancellationToken),
            type => ChangeOperation.Create(OperationKind.Update, ItemKind.Schema, type.Id, baseVersion!.Value, type.Version).Value,
            cancellationToken);
    }

    private const string DefinitionForm = "an object with a 'fields' array, such as {\"fields\": [{\"name\": \"title\", \"type\": \"text\"}]}";

    // Argument and definition errors come back together, before anything is read or written.
    private static ToolResult? Refused(ToolArgumentsReader arguments, List<Error> errors, System.Text.Json.JsonElement? definition, out ContentTypeDefinition? parsed)
    {
        errors.InsertRange(0, arguments.Errors());
        parsed = null;

        if (definition is { } json)
        {
            var read = ContentTypeDefinition.Parse(json.GetRawText());
            if (read.IsSuccess)
            {
                parsed = read.Value;
            }
            else
            {
                errors.AddRange(read.Errors);
            }
        }

        return errors.Count > 0 ? ToolResult.Failure(errors) : null;
    }

    private static async Task<ToolResult> ListAsync(ToolInvocation call, CancellationToken cancellationToken)
    {
        var arguments = new ToolArgumentsReader(call.Arguments);
        if (arguments.Errors() is { Count: > 0 } errors)
        {
            return ToolResult.Failure(errors);
        }

        var types = await call.Services.GetRequiredService<IContentTypeDirectory>().ListAsync(cancellationToken);
        return ToolResult.Success(new JsonObject { ["contentTypes"] = new JsonArray([.. types.Select(ToolJson.Summary)]) });
    }

    private static async Task<ToolResult> GetAsync(ToolInvocation call, CancellationToken cancellationToken)
    {
        var arguments = new ToolArgumentsReader(call.Arguments);
        var slug = arguments.RequiredString("slug", "product");
        var version = arguments.OptionalPositiveInteger("version");
        if (arguments.Errors() is { Count: > 0 } errors)
        {
            return ToolResult.Failure(errors);
        }

        var directory = call.Services.GetRequiredService<IContentTypeDirectory>();
        var type = version is { } number
            ? await directory.GetVersionAsync(slug!, number, cancellationToken)
            : await directory.GetAsync(slug!, cancellationToken);

        return ToolResult.From(type, ToolJson.ContentType, Hint(type));
    }

    private static async Task<ToolResult> SchemaDocumentAsync(ToolInvocation call, CancellationToken cancellationToken)
    {
        var arguments = new ToolArgumentsReader(call.Arguments);
        var slug = arguments.OptionalString("slug", "product");
        if (arguments.Errors() is { Count: > 0 } errors)
        {
            return ToolResult.Failure(errors);
        }

        var documents = call.Services.GetRequiredService<ISchemaDocuments>();
        if (slug is null)
        {
            return ToolResult.Success(await documents.GetSiteAsync(cancellationToken));
        }

        var document = await documents.GetTypeAsync(slug, cancellationToken);
        return ToolResult.From(document, value => value, Hint(document));
    }

    private static string? Hint<T>(Result<T> result)
    {
        return result.Errors.FirstOrDefault()?.Code switch
        {
            SchemaErrorCodes.ContentTypeNotFound => FindSlugs,
            SchemaErrorCodes.VersionNotFound => "Leave out the version to read the current one.",
            _ => null,
        };
    }
}
