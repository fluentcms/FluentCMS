using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Core.Content;
using FluentCMS.Core.Entities;
using FluentCMS.Core.Results;
using FluentCMS.Core.Schema;
using FluentCMS.Core.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Core.Builtin.Tools;

// Entities (ENT-01, ENT-02, ENT-04, MCP-03): their facts with sources and flags, their relations, the blast radius of changing them,
// and fact-level writes that go into a change set a person reviews. Whole entities are written with create_entry and update_entry.
public sealed class EntityTools : IToolProvider
{
    private const string TimeExample = "2026-01-31T09:30:00Z";

    public IReadOnlyList<AgentTool> Tools { get; } =
    [
        new AgentTool(
            "get_entity_facts",
            "Get an entity's facts",
            "Returns the facts of an entity's latest draft, or of the version you name: each field's source, owner, validity and "
            + "whether it is unsourced. Values are in get_entry. A fact without a source is flagged, and routing may hold it back.",
            ToolSchemas.Object(
                IdArgument,
                ToolSchemas.PositiveInteger("version", "A version to read instead of the latest draft.", 1, required: false)),
            readOnly: true,
            FactsAsync),
        new AgentTool(
            "get_entity_relations",
            "Get an entity's relations",
            "Lists the entities this one relates to through its reference fields, and the entities that relate to it, with the field "
            + "that holds each relation and whether the latest draft or the published version holds it. Pages that show the entity "
            + "are uses, not relations: get_where_used lists them.",
            ToolSchemas.Object(IdArgument),
            readOnly: true,
            RelationsAsync),
        new AgentTool(
            "list_unsourced_facts",
            "List unsourced facts",
            "Lists the facts without a source in the entities' latest drafts and published versions, of every entity type or of the "
            + "one you name. Give each a source with set_entity_fact.",
            ToolSchemas.Object(ToolSchemas.String("content_type", "An entity type's slug, to list only its facts.", "product", required: false)),
            readOnly: true,
            UnsourcedAsync),
        new AgentTool(
            "get_blast_radius",
            "Get a change set's blast radius",
            "Lists the published entries that show a fact the change set's entity changes change, such as every product page that "
            + "shows a changed price, each with the old and new value at its path. Read it before submitting a set of fact changes.",
            ToolSchemas.Object(ToolSchemas.Uuid("change_set_id", "The change set, as open_change_set or a write tool returned it.")),
            readOnly: true,
            BlastRadiusAsync),
        new AgentTool(
            "set_entity_fact",
            "Propose one fact of an entity",
            "Proposes a new value for one field of an entity, with its source, owner and validity, from the version you read as "
            + "base_version; the other facts stay as they are. It stores the next version and adds the change to a change set in "
            + "the same call. Every fact property you leave out is stored as null, and a fact without a source is flagged unsourced.",
            ToolSchemas.Object([
                IdArgument,
                BaseVersionArgument,
                ToolSchemas.String("field", "The entity field to set, as get_content_type lists it.", "price"),
                ToolSchemas.Json("value", "The fact's value, as the field's type takes it; get_schema_document shows each.", new JsonObject(), 24.99),
                ToolSchemas.Json(
                    "source",
                    "Where the value comes from: a kind (url, file, person or system) and a reference to it.",
                    new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            [FactMetadata.SourceKind] = new JsonObject { ["enum"] = new JsonArray([.. FactMetadata.SourceKinds.Select(kind => JsonValue.Create(kind))]) },
                            [FactMetadata.SourceReference] = new JsonObject { ["type"] = "string" },
                        },
                        ["required"] = new JsonArray(FactMetadata.SourceKind, FactMetadata.SourceReference),
                    },
                    new JsonObject { [FactMetadata.SourceKind] = "file", [FactMetadata.SourceReference] = "exports/catalog-2026-10-18.csv" },
                    required: false),
                ToolSchemas.Uuid("owner", "The id of the user who owns the fact.", required: false),
                ToolSchemas.String("valid_from", "When the value starts to hold, in ISO 8601 with an offset.", TimeExample, required: false),
                ToolSchemas.String("valid_until", "When the value stops holding, in ISO 8601 with an offset.", TimeExample, required: false),
                ToolSchemas.String("last_verified", "When someone last confirmed the value, in ISO 8601 with an offset.", TimeExample, required: false),
                .. ChangeSetTarget.Arguments]),
            readOnly: false,
            SetFactAsync),
        new AgentTool(
            "relate_entities",
            "Propose a relation between entities",
            "Proposes relating an entity to another through one of its reference fields, from the version you read as "
            + "base_version: a single reference is set to the other entity, and a list of references gets it added. It stores the "
            + "next version and adds the change to a change set in the same call; the field keeps its source and validity.",
            ToolSchemas.Object([
                IdArgument,
                BaseVersionArgument,
                ToolSchemas.String("field", "The reference field that holds the relation, such as category or specs.", "specs"),
                ToolSchemas.Uuid("related_id", "The entity to relate to, of the type the field references."),
                .. ChangeSetTarget.Arguments]),
            readOnly: false,
            RelateAsync),
    ];

    private static ToolArgument IdArgument { get; } = ToolSchemas.Uuid("id", "The entity's id, as list_entries or create_entry returned it.");

    private static ToolArgument BaseVersionArgument { get; } =
        ToolSchemas.PositiveInteger("base_version", "The latest version you read, as get_entry returned it.", 1);

    private static async Task<ToolResult> FactsAsync(ToolInvocation call, CancellationToken cancellationToken)
    {
        var arguments = new ToolArgumentsReader(call.Arguments);
        var id = arguments.RequiredGuid("id");
        var version = arguments.OptionalPositiveInteger("version");
        if (arguments.Errors() is { Count: > 0 } errors)
        {
            return ToolResult.Failure(errors);
        }

        var facts = await call.Services.GetRequiredService<IEntityFacts>().GetAsync(id!.Value, version, cancellationToken);
        return ToolResult.From(facts, ToolJson.Facts, ProposedEntryWrite.Hint(facts.Errors));
    }

    private static async Task<ToolResult> RelationsAsync(ToolInvocation call, CancellationToken cancellationToken)
    {
        var arguments = new ToolArgumentsReader(call.Arguments);
        var id = arguments.RequiredGuid("id");
        if (arguments.Errors() is { Count: > 0 } errors)
        {
            return ToolResult.Failure(errors);
        }

        var relations = await call.Services.GetRequiredService<IEntityRelations>().GetAsync(id!.Value, cancellationToken);
        return ToolResult.From(relations, ToolJson.Relations, ProposedEntryWrite.Hint(relations.Errors));
    }

    private static async Task<ToolResult> UnsourcedAsync(ToolInvocation call, CancellationToken cancellationToken)
    {
        var arguments = new ToolArgumentsReader(call.Arguments);
        var contentType = arguments.OptionalString("content_type", "product");
        if (arguments.Errors() is { Count: > 0 } errors)
        {
            return ToolResult.Failure(errors);
        }

        var flags = await call.Services.GetRequiredService<IEntityFacts>().UnsourcedAsync(contentType, cancellationToken);
        return ToolResult.From(
            flags,
            list => new JsonObject { ["facts"] = new JsonArray([.. list.Select(ToolJson.Flag)]) },
            flags.Errors.FirstOrDefault()?.Code == EntityErrorCodes.TypeNotEntity
                ? "Only entity types have facts; get_content_type shows whether a type is one."
                : ProposedEntryWrite.Hint(flags.Errors));
    }

    private static async Task<ToolResult> BlastRadiusAsync(ToolInvocation call, CancellationToken cancellationToken)
    {
        var arguments = new ToolArgumentsReader(call.Arguments);
        var id = arguments.RequiredGuid("change_set_id");
        if (arguments.Errors() is { Count: > 0 } errors)
        {
            return ToolResult.Failure(errors);
        }

        var radius = await call.Services.GetRequiredService<IBlastRadius>().ForChangeSetAsync(id!.Value, cancellationToken);
        return ToolResult.From(radius, ToolJson.BlastRadius);
    }

    private static async Task<ToolResult> SetFactAsync(ToolInvocation call, CancellationToken cancellationToken)
    {
        var arguments = new ToolArgumentsReader(call.Arguments);
        var id = arguments.RequiredGuid("id");
        var baseVersion = arguments.RequiredPositiveInteger("base_version");
        var field = arguments.RequiredString("field", "price");
        var value = arguments.RequiredValue("value", "the fact's value, such as 24.99");
        var source = arguments.OptionalObject("source", "{\"kind\": \"file\", \"reference\": \"exports/catalog.csv\"}");
        var owner = arguments.OptionalGuid("owner");
        var validFrom = arguments.OptionalString("valid_from", TimeExample);
        var validUntil = arguments.OptionalString("valid_until", TimeExample);
        var lastVerified = arguments.OptionalString("last_verified", TimeExample);
        var errors = new List<Error>();
        var target = ChangeSetTarget.Read(arguments, errors);
        errors.InsertRange(0, arguments.Errors());
        if (errors.Count > 0)
        {
            return ToolResult.Failure(errors);
        }

        var fact = new JsonObject
        {
            [FactMetadata.Value] = JsonNode.Parse(value!.Value.GetRawText()),
            [FactMetadata.Source] = source is { } given ? JsonNode.Parse(given.GetRawText()) : null,
            [FactMetadata.Owner] = owner?.ToString(),
            [FactMetadata.ValidFrom] = validFrom,
            [FactMetadata.ValidUntil] = validUntil,
            [FactMetadata.LastVerified] = lastVerified,
        };

        return await ProposedEntryWrite.RunAsync(
            call,
            target!,
            entries => ReviseEntityAsync(call.Services, entries, id!.Value, baseVersion!.Value, (_, fields) =>
            {
                fields[field!] = fact.DeepClone();
                return Result.Success();
            }, cancellationToken),
            ProposedEntryWrite.Update,
            cancellationToken);
    }

    private static async Task<ToolResult> RelateAsync(ToolInvocation call, CancellationToken cancellationToken)
    {
        var arguments = new ToolArgumentsReader(call.Arguments);
        var id = arguments.RequiredGuid("id");
        var baseVersion = arguments.RequiredPositiveInteger("base_version");
        var field = arguments.RequiredString("field", "specs");
        var relatedId = arguments.RequiredGuid("related_id");
        var errors = new List<Error>();
        var target = ChangeSetTarget.Read(arguments, errors);
        errors.InsertRange(0, arguments.Errors());
        if (errors.Count > 0)
        {
            return ToolResult.Failure(errors);
        }

        var fieldTypes = call.Services.GetRequiredService<FieldTypeRegistry>();

        return await ProposedEntryWrite.RunAsync(
            call,
            target!,
            entries => ReviseEntityAsync(call.Services, entries, id!.Value, baseVersion!.Value, (definition, fields) =>
            {
                var relation = definition.Fields.FirstOrDefault(candidate => candidate.Name == field);
                if (relation is null || fieldTypes.ReferenceTargets(relation.Type, relation.Settings).Count == 0)
                {
                    return Result.Failure(
                        EntityErrorCodes.FieldNotRelation,
                        $"'{field}' is not a reference field of this entity type; its reference fields are "
                        + string.Join(", ", definition.Fields.Where(candidate => fieldTypes.ReferenceTargets(candidate.Type, candidate.Settings).Count > 0).Select(candidate => candidate.Name))
                        + ".");
                }

                var fact = fields[field!] as JsonObject ?? EmptyFact();
                var related = relatedId!.Value.ToString();
                if (relation.Type == "list")
                {
                    var list = fact[FactMetadata.Value] as JsonArray ?? [];
                    if (!list.Any(item => item?.GetValue<string>() is { } existing && Guid.TryParse(existing, out var parsed) && parsed == relatedId))
                    {
                        list.Add(related);
                    }

                    fact[FactMetadata.Value] = list.DeepClone();
                }
                else
                {
                    fact[FactMetadata.Value] = related;
                }

                fields[field!] = fact.DeepClone();
                return Result.Success();
            }, cancellationToken),
            ProposedEntryWrite.Update,
            cancellationToken);
    }

    // Reads the entity at the version the agent read, changes its fields in a copy and writes the next version; a stale base version
    // fails on the write as for update_entry.
    private static async Task<Result<Entry>> ReviseEntityAsync(
        IServiceProvider services,
        IEntryDirectory entries,
        Guid id,
        int baseVersion,
        Func<ContentTypeDefinition, JsonObject, Result> change,
        CancellationToken cancellationToken)
    {
        var entry = await entries.GetAsync(id, cancellationToken);
        if (!entry.IsSuccess)
        {
            return entry;
        }

        var type = await services.GetRequiredService<IContentTypeDirectory>().GetAsync(entry.Value.ContentType, cancellationToken);
        if (!type.IsSuccess)
        {
            return Result<Entry>.Failure(type.Errors);
        }

        if (!type.Value.Definition.IsEntity)
        {
            return Result<Entry>.Failure(
                ContentErrorCodes.EntryNotEntity,
                $"Entry {id} is a '{entry.Value.ContentType}', which is not an entity type, so it has no facts; use update_entry.");
        }

        var read = await entries.GetVersionAsync(id, baseVersion, cancellationToken);
        if (!read.IsSuccess)
        {
            return Result<Entry>.Failure(read.Errors);
        }

        var fields = JsonNode.Parse(read.Value.Fields.GetRawText())!.AsObject();
        var changed = change(type.Value.Definition, fields);
        if (!changed.IsSuccess)
        {
            return Result<Entry>.Failure(changed.Errors);
        }

        return await entries.UpdateAsync(id, baseVersion, JsonSerializer.SerializeToElement(fields), cancellationToken);
    }

    private static JsonObject EmptyFact()
    {
        return new JsonObject
        {
            [FactMetadata.Value] = null,
            [FactMetadata.Source] = null,
            [FactMetadata.Owner] = null,
            [FactMetadata.ValidFrom] = null,
            [FactMetadata.ValidUntil] = null,
            [FactMetadata.LastVerified] = null,
        };
    }
}
