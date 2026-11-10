using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Agents;
using FluentCMS.Authorization;
using FluentCMS.Content;
using FluentCMS.Schema;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluentCMS.Plugins.Mcp;

// What the content tools reach for the caller: what it may do, and the exposed types it may read.
internal sealed record McpReach(ContentAccess Access, IReadOnlyList<SchemaType> Exposed)
{
    public bool Drafts(SchemaType type) => Access.Types[type.Name].Drafts;
}

// The dry runs of spec 5.4, in front of {type}_delete and bulk. A call is a dry run unless it says dryRun false: it
// changes nothing and answers the plan, which is the entries the call would touch, the references that would break and
// a confirmation token. The run needs that token, and the plan is read again before it: a token that is missing,
// expired, of another call, principal, client or site, or of a plan that has changed since is refused and nothing runs.
// Everything is read and written through the content service, as the caller.
internal sealed class McpDryRuns(
    IContentService content,
    IContentSurface surface,
    McpConfirmations confirmations,
    IOptions<McpOptions> options,
    ILogger<McpDryRuns> logger)
{
    public const string Delete = "delete";

    // The most references a plan lists. A plan that may have more says so.
    public const int MaxReferences = 50;

    public static readonly string[] BulkArguments = ["type", "action", "ids", "filter", "limit", "locale", McpConfirmations.DryRun, McpConfirmations.Token];

    private static readonly string[] FilterOnly = [McpQueryArguments.Filter];

    public async Task<McpToolResult> DeleteAsync(McpToolCall call, SchemaType type, McpReach reach, CancellationToken cancellationToken)
    {
        var arguments = call.Arguments;
        McpArguments.Only(arguments, "id", McpConfirmations.DryRun, McpConfirmations.Token);
        var id = McpArguments.Id(arguments);
        var (dryRun, token) = Run(arguments);
        if (!dryRun && token is null)
        {
            return Refusal(call, McpConfirmation.Missing)!;
        }

        var entries = await EntriesAsync(type, reach, new FieldFilter("id", FilterOperator.Equal, id), locale: null, LocaleFallback.Chain, max: 1, cancellationToken);
        if (entries.Count == 0)
        {
            throw new EntryNotFoundException(type.Name, id);
        }

        var plan = PlanOf(call, entries);
        if (dryRun)
        {
            return await DescribeAsync(plan, type, Delete, entries, notFound: [], reach, cancellationToken);
        }

        if (Refusal(call, confirmations.Check(token, plan)) is { } refused)
        {
            return refused;
        }

        await content.DeleteAsync(type.Name, id, cancellationToken);
        return McpArguments.Result(new JsonObject { ["type"] = type.Name, ["id"] = id, ["deleted"] = true });
    }

    // The entries are named by ids or by a filter, and never more than the limit the call sets, which the server caps. A
    // filter is read here, for the plan and again for the run, and the run then names the entries of the plan by id, so
    // that it touches those the caller confirmed and no other.
    public async Task<McpToolResult> BulkAsync(McpToolCall call, SchemaType type, McpReach reach, CancellationToken cancellationToken)
    {
        var arguments = call.Arguments;
        var action = McpArguments.RequiredText(arguments, "action");
        BulkOperation operation = action switch
        {
            McpContentTools.Publish => BulkOperation.Publish,
            McpContentTools.Unpublish => BulkOperation.Unpublish,
            Delete => BulkOperation.Delete,
            _ => throw McpArguments.Invalid("action", $"'action' is {McpContentTools.Publish}, {McpContentTools.Unpublish} or {Delete}."),
        };

        var max = options.Value.MaxBulkItems;
        var limit = McpArguments.Integer(arguments, "limit") ?? throw McpArguments.Required("limit");
        if (limit < 1)
        {
            throw McpArguments.Invalid("limit", "'limit' is 1 or more.");
        }

        if (limit > max)
        {
            return McpToolResult.Error(McpErrorCodes.BulkLimitExceeded, $"'limit' is {limit}, and a bulk call takes {max} entries at most ({McpOptions.Section}:MaxBulkItems).");
        }

        var ids = Ids(arguments);
        var filtered = McpArguments.Object(arguments, McpQueryArguments.Filter) is not null;
        if (ids is null != filtered)
        {
            throw McpArguments.Invalid(filtered ? McpQueryArguments.Filter : "ids", "A bulk call names its entries by 'ids' or by 'filter', one of the two.");
        }

        var filter = filtered ? surface.ParseQuery(McpQueryArguments.Parameters(arguments, FilterOnly)).Filter : null;
        if (filtered && filter is null)
        {
            throw McpArguments.Invalid(McpQueryArguments.Filter, "'filter' holds a condition at least; an empty one would reach every entry.");
        }

        if (ids is { Count: 0 })
        {
            throw McpArguments.Invalid("ids", "'ids' names an entry at least.");
        }

        if (ids?.Count > limit)
        {
            return McpToolResult.Error(McpErrorCodes.BulkLimitExceeded, $"'ids' names {ids.Count} entries, more than the limit of {limit}.");
        }

        var locale = McpArguments.Text(arguments, "locale");
        if (locale is not null && operation == BulkOperation.Delete)
        {
            throw McpArguments.Invalid("locale", "'locale' goes with publish and unpublish; a delete moves an entry to the trash in every locale.");
        }

        var (dryRun, token) = Run(arguments);
        if (!dryRun && token is null)
        {
            return Refusal(call, McpConfirmation.Missing)!;
        }

        // A publish or an unpublish is of one locale, so an entry counts when it has that locale. A delete by id finds
        // the entry as {type}_get shows it.
        var fallback = operation == BulkOperation.Delete && ids is not null ? LocaleFallback.Chain : LocaleFallback.None;
        var entries = await EntriesAsync(type, reach, filter ?? new FieldFilter("id", FilterOperator.In, ids!.ToList()), locale, fallback, limit, cancellationToken);
        if (entries.Count > limit)
        {
            return McpToolResult.Error(McpErrorCodes.BulkLimitExceeded, $"The filter matches more than the limit of {limit} entries, so nothing ran. Narrow the filter, or raise 'limit' up to {max}.");
        }

        var found = entries.Select(entry => entry.Id).ToHashSet();
        List<Guid> notFound = [.. (ids ?? []).Where(id => !found.Contains(id))];
        var plan = PlanOf(call, entries);
        if (dryRun)
        {
            return await DescribeAsync(plan, type, action, entries, notFound, reach, cancellationToken);
        }

        if (Refusal(call, confirmations.Check(token, plan)) is { } refused)
        {
            return refused;
        }

        // A publish names the draft version of the plan, so a draft changed after the plan was read again is a conflict.
        var expectsVersion = operation == BulkOperation.Publish && reach.Drafts(type);
        var request = new BulkRequest
        {
            Operation = operation,
            Items = [.. entries.Select(entry => new BulkItem(entry.Id) { ExpectedVersion = expectsVersion ? entry.Version : null })],
            Locale = locale,
        };
        var result = entries.Count > 0 ? await content.BulkAsync(type.Name, request, cancellationToken) : new BulkResult([], Cancelled: false);
        var answer = surface.ToJson(result);
        AddNotFound(answer, notFound);
        return McpArguments.Result(answer);
    }

    private static (bool DryRun, string? Token) Run(JsonElement arguments) =>
        (McpArguments.Flag(arguments, McpConfirmations.DryRun) ?? true, McpArguments.Text(arguments, McpConfirmations.Token) is { Length: > 0 } token ? token : null);

    private static McpPlan PlanOf(McpToolCall call, List<PlannedEntry> entries) =>
        new(call.Operation, call.Name, call.Arguments, [.. entries.Select(entry => new McpPlanEntry(entry.Id, entry.Version, entry.Status))]);

    private static List<Guid>? Ids(JsonElement arguments)
    {
        if (McpArguments.Texts(arguments, "ids") is not { } texts)
        {
            return null;
        }

        List<Guid> ids = [];
        foreach (var text in texts)
        {
            ids.Add(Guid.TryParseExact(text, "D", out var id) ? id : throw McpArguments.Invalid("ids", "'ids' is an array of ids, such as 0192f2a4-7b1c-7d2e-9f10-2a3b4c5d6e7f."));
        }

        return [.. ids.Distinct()];
    }

    private McpToolResult? Refusal(McpToolCall call, McpConfirmation confirmation)
    {
        var (code, message) = confirmation switch
        {
            McpConfirmation.Confirmed => (null, ""),
            McpConfirmation.Missing => (
                McpErrorCodes.ConfirmationRequired,
                $"The call changes data, so it needs a confirmation. Call it without '{McpConfirmations.DryRun}' to get the plan and its '{McpConfirmations.Token}', then again with '{McpConfirmations.DryRun}' false and that token."),
            McpConfirmation.Expired => (McpErrorCodes.ConfirmationExpired, "The confirmation token has expired; one lasts five minutes. Run the dry run again."),
            McpConfirmation.PlanChanged => (
                McpErrorCodes.PlanChanged,
                "The plan has changed since the dry run: an entry has a new version or status, or the call reaches other entries now. Nothing ran. Run the dry run again and look at the new plan."),
            _ => (
                McpErrorCodes.ConfirmationInvalid,
                "The confirmation token was not made for this call. A token is of one tool, its arguments, and the principal, the client and the site of the dry run that gave it. Run the dry run again."),
        };
        if (code is null)
        {
            return null;
        }

        var operation = call.Operation;
        McpLog.ConfirmationRefused(logger, call.Name, operation.Principal.Kind, operation.Principal.Id, operation.ClientId, operation.Site.Value, code);
        return McpToolResult.Error(code, message);
    }

    private async Task<McpToolResult> DescribeAsync(McpPlan plan, SchemaType type, string action, List<PlannedEntry> entries, List<Guid> notFound, McpReach reach, CancellationToken cancellationToken)
    {
        JsonObject described = new()
        {
            [McpConfirmations.DryRun] = true,
            ["type"] = type.Name,
            ["action"] = action,
            ["count"] = entries.Count,
            ["entries"] = new JsonArray([.. entries.Select(entry => (JsonNode)entry.Json)]),
        };
        AddNotFound(described, notFound);

        // A publish breaks nothing. An unpublish takes the entry from the readers of what is published, and a delete
        // from every reader.
        var (references, more) = action == McpContentTools.Publish || entries.Count == 0
            ? ([], false)
            : await ReferencesAsync(type, entries, published: action == McpContentTools.Unpublish, reach, cancellationToken);
        described["references"] = references;
        if (more)
        {
            described["moreReferences"] = true;
        }

        // With no entry there is nothing to confirm.
        if (entries.Count > 0)
        {
            var (token, expiresAt) = confirmations.Issue(plan);
            described[McpConfirmations.Token] = token;
            described["expiresAt"] = expiresAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        }

        return McpArguments.Result(described);
    }

    private static void AddNotFound(JsonObject answer, List<Guid> notFound)
    {
        if (notFound.Count > 0)
        {
            answer["notFound"] = new JsonArray([.. notFound.Select(id => (JsonNode)id)]);
        }
    }

    // The entries a filter selects as the caller reads them, in the order of their ids: one more than the most asked for
    // at the most, so that the caller can tell a filter that reaches too many.
    private async Task<List<PlannedEntry>> EntriesAsync(SchemaType type, McpReach reach, ContentFilter filter, string? locale, LocaleFallback fallback, int max, CancellationToken cancellationToken)
    {
        var title = TitleOf(type);
        var query = new ContentQuery
        {
            Filter = filter,
            Select = title is null ? ["id", "status", "version"] : ["id", "status", "version", title],
            Sort = [new SortField("id")],
            Status = reach.Drafts(type) ? QueryStatus.Draft : QueryStatus.Published,
            Locale = type.Localized ? locale : null,
            Fallback = fallback,
        };

        List<PlannedEntry> found = [];
        string? cursor = null;
        do
        {
            var size = Math.Min(surface.MaxPageSize, max + 1 - found.Count);
            var page = await content.QueryAsync(type.Name, query with { Cursor = cursor, Page = new PageRequest { Size = size, WithTotal = false } }, cancellationToken);
            found.AddRange(page.Items.Select(entry => PlannedEntry.Of(entry, surface.ToJson(entry), title)));
            cursor = page.NextCursor;
        }
        while (cursor is not null && found.Count <= max);

        return found;
    }

    // The entries of the exposed types the caller may read whose relation points to an entry of the plan: the fields of a
    // type itself, and not the relations inside its components and blocks. An entry of the plan is left out, since it goes
    // with the others.
    private async Task<(JsonArray References, bool More)> ReferencesAsync(SchemaType target, List<PlannedEntry> entries, bool published, McpReach reach, CancellationToken cancellationToken)
    {
        JsonArray references = [];
        var more = false;
        var planned = entries.Select(entry => entry.Id).ToHashSet();
        foreach (var type in reach.Exposed)
        {
            var title = TitleOf(type);
            foreach (var field in type.Fields.Where(field => !field.Hidden && field.Relation?.Target == target.Name))
            {
                if (references.Count >= MaxReferences)
                {
                    return (references, true);
                }

                var query = new ContentQuery
                {
                    Filter = new FieldFilter(field.Name, FilterOperator.In, planned.ToList()),
                    Select = title is null ? ["id"] : ["id", title],
                    Sort = [new SortField("id")],
                    Status = published || !reach.Drafts(type) ? QueryStatus.Published : QueryStatus.Draft,
                    Page = new PageRequest { Size = Math.Min(surface.MaxPageSize, MaxReferences + planned.Count), WithTotal = false },
                };

                PagedResult<ContentDocument> page;
                try
                {
                    page = await content.QueryAsync(type.Name, query, cancellationToken);
                }
                catch (ValidationException)
                {
                    // A relation the engine does not filter by is not looked through, and the plan says it may miss some.
                    more = true;
                    continue;
                }

                more = more || page.NextCursor is not null;
                foreach (var entry in page.Items.Where(entry => !(type.Name == target.Name && planned.Contains(entry.Get<Guid>("id")))))
                {
                    if (references.Count >= MaxReferences)
                    {
                        more = true;
                        break;
                    }

                    var json = surface.ToJson(entry);
                    references.Add(new JsonObject
                    {
                        ["type"] = type.Name,
                        ["id"] = json["id"]?.DeepClone(),
                        ["title"] = title is null ? null : json[title]?.DeepClone(),
                        ["field"] = field.Name,
                    });
                }
            }
        }

        return (references, more);
    }

    // The schema names no title, so it is the text field called title or name, or else the first text field.
    private static string? TitleOf(SchemaType type)
    {
        var texts = type.Fields.Where(field => !field.Hidden && !field.Multiple && field.FieldType == "text").ToList();
        return (texts.FirstOrDefault(field => field.Name == "title") ?? texts.FirstOrDefault(field => field.Name == "name") ?? texts.FirstOrDefault())?.Name;
    }

    private sealed record PlannedEntry(Guid Id, int? Version, string? Status, JsonObject Json)
    {
        public static PlannedEntry Of(ContentDocument entry, JsonObject json, string? title) => new(
            entry.Get<Guid>("id"),
            entry.TryGetValue("version", out var version) && version is int number ? number : null,
            json["status"]?.ToString(),
            new JsonObject
            {
                ["id"] = json["id"]?.DeepClone(),
                ["title"] = title is null ? null : json[title]?.DeepClone(),
                ["status"] = json["status"]?.DeepClone(),
                ["version"] = json["version"]?.DeepClone(),
            });
    }
}
