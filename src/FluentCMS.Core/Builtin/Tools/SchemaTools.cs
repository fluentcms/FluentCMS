using System.Text.Json.Nodes;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Builtin.Tools;

// Reading the schema (SCH-01, SCH-06, MCP-03): the content types of the site and the schema document entries are written from.
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

    private static async Task<ToolResult> ListAsync(ToolCall call, CancellationToken cancellationToken)
    {
        var arguments = new ToolArgumentsReader(call.Arguments);
        if (arguments.Errors() is { Count: > 0 } errors)
        {
            return ToolResult.Failure(errors);
        }

        var types = await call.Services.GetRequiredService<IContentTypeDirectory>().ListAsync(cancellationToken);
        return ToolResult.Success(new JsonObject { ["contentTypes"] = new JsonArray([.. types.Select(ToolJson.Summary)]) });
    }

    private static async Task<ToolResult> GetAsync(ToolCall call, CancellationToken cancellationToken)
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

    private static async Task<ToolResult> SchemaDocumentAsync(ToolCall call, CancellationToken cancellationToken)
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
