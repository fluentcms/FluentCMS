using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Agents;
using FluentCMS.Authorization;
using FluentCMS.Content;
using FluentCMS.Schema;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluentCMS.Plugins.Mcp;

internal static class McpTools
{
    public static void Add(IPluginServiceBuilder plugin)
    {
        plugin.Services.AddSingleton<McpTypeCap>();
        plugin.Services.AddSingleton<McpDownloads>();
        plugin.AddMcpTools<McpContentTools>().AddMcpTools<McpMediaTools>();
    }
}

// The types over the cap that the log has named, so that each is named once and not on every tool list.
internal sealed class McpTypeCap
{
    private readonly ConcurrentDictionary<string, bool> _named = new(StringComparer.Ordinal);

    public bool Name(string type) => _named.TryAdd(type, true);
}

// The content tools of spec 5.4. Each exposed type the caller may read gets {type}_list, _get, _search, _create_draft,
// _update_draft, _discard_draft and _delete, a singleton _get, _update_draft and _discard_draft, and the shared tools
// publish, unpublish, schedule and query take a type. The list holds a tool only when core says a grant of the caller
// gives its permission, and a write tool only the fields the caller may write. A call goes to the content service as the
// caller, whatever the list held, so a tool called by name without its permission is refused there; the plugin asks no
// permission and reads no table itself.
internal sealed class McpContentTools(
    IContentService content,
    IEffectiveAccess access,
    IContentApiFormat format,
    IFieldTypeRegistry fieldTypes,
    IOptions<McpOptions> options,
    McpTypeCap cap,
    ILogger<McpContentTools> logger) : IMcpToolProvider
{
    public const string Publish = "publish";
    public const string Unpublish = "unpublish";
    public const string Schedule = "schedule";
    public const string Query = "query";

    public const string List = "list";
    public const string Get = "get";
    public const string Search = "search";
    public const string CreateDraft = "create_draft";
    public const string UpdateDraft = "update_draft";
    public const string DiscardDraft = "discard_draft";
    public const string Delete = "delete";

    private static readonly string[] Verbs = [List, Get, Search, CreateDraft, UpdateDraft, DiscardDraft, Delete];

    private static readonly McpToolAnnotations Reads = new() { ReadOnly = true, Idempotent = true };
    private static readonly McpToolAnnotations Adds = new() { Destructive = false };
    private static readonly McpToolAnnotations Changes = new();

    public static string Name(string type, string verb) => $"{type}_{verb}";

    public async ValueTask<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(McpListRequest request, CancellationToken cancellationToken = default)
    {
        var described = await access.ContentAsync(cancellationToken);
        var exposed = described.Schema.ContentTypes.Where(type => IsExposed(type.Name)).ToList();
        var max = options.Value.MaxExposedTypes;
        foreach (var type in exposed.Skip(max).Where(type => cap.Name(type.Name)))
        {
            McpLog.TypeOverCap(logger, type.Name, max);
        }

        List<McpToolDescriptor> tools = [];
        var schemas = new McpToolSchemas(described.Schema, fieldTypes);
        foreach (var type in exposed.Take(max))
        {
            AddTypeTools(tools, schemas, type, described.Types[type.Name], described.Search);
        }

        AddSharedTools(tools, exposed, described);
        return tools;
    }

    public async ValueTask<McpToolResult?> CallToolAsync(McpToolCall request, CancellationToken cancellationToken = default)
    {
        var arguments = request.Arguments;
        switch (request.Name)
        {
            case Publish:
                return await PublishAsync(arguments, cancellationToken);
            case Unpublish:
                return await UnpublishAsync(arguments, cancellationToken);
            case Schedule:
                return await ScheduleAsync(arguments, cancellationToken);
            case Query:
                return await QueryAsync(arguments, cancellationToken);
        }

        if (Split(request.Name) is not var (typeName, verb))
        {
            return null;
        }

        // A type the caller may not read has no tools, as a type that does not exist has none.
        var described = await access.ContentAsync(cancellationToken);
        if (!IsExposed(typeName) || described.Schema.FindContentType(typeName) is not { } type)
        {
            return null;
        }

        var drafts = described.Types[type.Name].Drafts;
        if (type.Kind == SchemaTypeKind.Singleton)
        {
            return verb switch
            {
                Get => await GetSingletonAsync(type, arguments, drafts, cancellationToken),
                UpdateDraft => await UpdateSingletonAsync(type, arguments, cancellationToken),
                DiscardDraft => await DiscardSingletonAsync(type, arguments, cancellationToken),
                _ => null,
            };
        }

        return verb switch
        {
            List => await ListAsync(type.Name, arguments, McpQueryArguments.List, drafts, cancellationToken),
            Get => await GetAsync(type.Name, arguments, drafts, cancellationToken),
            Search => await SearchAsync(type.Name, arguments, drafts, cancellationToken),
            CreateDraft => await CreateAsync(type.Name, arguments, cancellationToken),
            UpdateDraft => await UpdateAsync(type.Name, arguments, cancellationToken),
            DiscardDraft => await DiscardAsync(type.Name, McpArguments.Id(arguments), arguments, ["id", "expectedVersion", "locale"], cancellationToken),
            Delete => await DeleteAsync(type.Name, arguments, cancellationToken),
            _ => null,
        };
    }

    private static void AddTypeTools(List<McpToolDescriptor> tools, McpToolSchemas schemas, SchemaType type, ContentTypeAccess may, bool search)
    {
        var name = type.Name;
        var fields = McpToolSchemas.FieldList(type);
        if (type.Kind == SchemaTypeKind.Singleton)
        {
            tools.Add(Tool(name, Get, $"Reads the {name} singleton, which has one entry. Fields: {fields}.", McpToolSchemas.Get(type, may.Drafts), Reads, $"Get {name}"));
            if (may.Update)
            {
                tools.Add(Tool(
                    name,
                    UpdateDraft,
                    $"Changes fields of the draft of the {name} singleton, or creates its entry when it has none. The fields sent replace their values and the others stay. Nothing is published.",
                    schemas.Update(type, may.UpdatableFields),
                    Changes,
                    $"Update {name} draft"));
                tools.Add(Tool(name, DiscardDraft, $"Discards the draft changes of the {name} singleton and goes back to what is published.", McpToolSchemas.Discard(type), Changes, $"Discard {name} draft"));
            }

            return;
        }

        tools.Add(Tool(
            name,
            List,
            $"Lists the entries of the content type {name}{(may.Drafts ? " as drafts, or as published" : " that are published")}, with filters, sorting and paging. Fields: {fields}.",
            McpToolSchemas.List(type, may.Drafts, search: false),
            Reads,
            $"List {name}"));
        tools.Add(Tool(name, Get, $"Reads one {name} entry by its id.", McpToolSchemas.Get(type, may.Drafts), Reads, $"Get {name}"));
        if (search)
        {
            tools.Add(Tool(name, Search, $"Searches the {name} entries by text, with the filters, sorting and paging of {Name(name, List)}.", McpToolSchemas.List(type, may.Drafts, search: true), Reads, $"Search {name}"));
        }

        if (may.Create)
        {
            tools.Add(Tool(
                name,
                CreateDraft,
                $"Creates a {name} entry as a draft, in the locale of the connection. Nothing is published.",
                schemas.Create(type, may.CreatableFields),
                Adds,
                $"Create {name} draft"));
        }

        if (may.Update)
        {
            tools.Add(Tool(
                name,
                UpdateDraft,
                $"Changes fields of the draft of a {name} entry, in the locale of the connection. The fields sent replace their values and the others stay. Nothing is published.",
                schemas.Update(type, may.UpdatableFields),
                Changes,
                $"Update {name} draft"));
            tools.Add(Tool(name, DiscardDraft, $"Discards the draft changes of a {name} entry and goes back to what is published.", McpToolSchemas.Discard(type), Changes, $"Discard {name} draft"));
        }

        if (may.Delete)
        {
            tools.Add(Tool(name, Delete, $"Moves a {name} entry to the trash, in every locale.", McpToolSchemas.Delete(type), Changes, $"Delete {name}"));
        }
    }

    // A shared tool is listed when the caller holds its permission for some exposed type, and names those types.
    private static void AddSharedTools(List<McpToolDescriptor> tools, List<SchemaType> exposed, ContentAccess described)
    {
        List<string> Types(Func<ContentTypeAccess, bool> may) => [.. exposed.Where(type => may(described.Types[type.Name])).Select(type => type.Name)];

        var publishable = Types(may => may.Publish);
        if (publishable.Count > 0)
        {
            tools.Add(new McpToolDescriptor(Publish, "Publishes the draft of an entry. The answer holds the entry and the warnings, such as a reference to an entry that is not published.", McpToolSchemas.Publish(publishable))
            {
                Title = "Publish",
                Annotations = Changes,
            });
        }

        var unpublishable = Types(may => may.Unpublish);
        if (unpublishable.Count > 0)
        {
            tools.Add(new McpToolDescriptor(Unpublish, "Takes an entry out of what is published. Its draft stays.", McpToolSchemas.Unpublish(unpublishable))
            {
                Title = "Unpublish",
                Annotations = Changes,
            });
        }

        if (publishable.Count > 0 || unpublishable.Count > 0)
        {
            List<string> actions = [.. new[] { Publish, Unpublish }.Where(action => (action == Publish ? publishable : unpublishable).Count > 0)];
            tools.Add(new McpToolDescriptor(
                Schedule,
                "Schedules a publish or an unpublish of an entry for a later time. A publish publishes the draft as it is when scheduled.",
                McpToolSchemas.Schedule([.. publishable.Union(unpublishable, StringComparer.Ordinal)], actions))
            {
                Title = "Schedule",
                Annotations = Changes,
            });
        }

        if (exposed.Count > 0)
        {
            tools.Add(new McpToolDescriptor(
                Query,
                "Lists the entries of any content type the caller may read, with the filters, sorting and paging of the REST APIs. It reaches the types that have no tools of their own.",
                McpToolSchemas.Query([.. exposed.Select(type => type.Name)], described.Search))
            {
                Title = "Query",
                Annotations = Reads,
            });
        }
    }

    private static McpToolDescriptor Tool(string type, string verb, string description, JsonElement inputSchema, McpToolAnnotations annotations, string title) =>
        new(Name(type, verb), description, inputSchema) { Title = title, ContentType = type, Annotations = annotations };

    private async Task<McpToolResult> ListAsync(string type, JsonElement arguments, string[] names, bool drafts, CancellationToken cancellationToken)
    {
        McpArguments.Only(arguments, names);
        var query = format.ParseQuery(McpQueryArguments.Parameters(arguments, names));
        return McpArguments.Result(format.List(await content.QueryAsync(type, WithStatus(query, drafts), cancellationToken)));
    }

    private Task<McpToolResult> SearchAsync(string type, JsonElement arguments, bool drafts, CancellationToken cancellationToken)
    {
        McpArguments.RequiredText(arguments, McpQueryArguments.Search);
        return ListAsync(type, arguments, [McpQueryArguments.Search, .. McpQueryArguments.List], drafts, cancellationToken);
    }

    private async Task<McpToolResult> GetAsync(string type, JsonElement arguments, bool drafts, CancellationToken cancellationToken)
    {
        McpArguments.Only(arguments, ["id", .. McpQueryArguments.Single]);
        var id = McpArguments.Id(arguments);
        var query = WithStatus(format.ParseQuery(McpQueryArguments.Parameters(arguments, McpQueryArguments.Single)), drafts);
        var entry = await content.GetAsync(type, id, OptionsOf(query), cancellationToken) ?? throw new EntryNotFoundException(type, id);
        return McpArguments.Result(format.Entry(entry));
    }

    private async Task<McpToolResult> CreateAsync(string type, JsonElement arguments, CancellationToken cancellationToken)
    {
        McpArguments.Only(arguments, "fields");
        var fields = McpArguments.Object(arguments, "fields") ?? throw McpArguments.Required("fields");
        return McpArguments.Result(format.Entry(await content.CreateAsync(type, McpArguments.Document(fields), cancellationToken)));
    }

    // A merge patch: a tool that replaced the draft would clear every field the caller left out.
    private async Task<McpToolResult> UpdateAsync(string type, JsonElement arguments, CancellationToken cancellationToken)
    {
        McpArguments.Only(arguments, "id", "expectedVersion", "fields");
        var id = McpArguments.Id(arguments);
        var expected = McpArguments.Integer(arguments, "expectedVersion") ?? throw McpArguments.Required("expectedVersion");
        var fields = McpArguments.Object(arguments, "fields") ?? throw McpArguments.Required("fields");
        return McpArguments.Result(format.Entry(await content.PatchAsync(type, id, McpArguments.Document(fields), expected, cancellationToken)));
    }

    private async Task<McpToolResult> DiscardAsync(string type, Guid id, JsonElement arguments, string[] names, CancellationToken cancellationToken)
    {
        McpArguments.Only(arguments, names);
        var options = new LocaleWriteOptions { Locale = McpArguments.Text(arguments, "locale"), ExpectedVersion = McpArguments.Integer(arguments, "expectedVersion") };
        return McpArguments.Result(format.Entry(await content.DiscardDraftAsync(type, id, options, cancellationToken)));
    }

    // The entry goes to the trash at once. What a delete takes before it runs, such as a dry run, is read here.
    private async Task<McpToolResult> DeleteAsync(string type, JsonElement arguments, CancellationToken cancellationToken)
    {
        McpArguments.Only(arguments, "id");
        var id = McpArguments.Id(arguments);
        await content.DeleteAsync(type, id, cancellationToken);
        return McpArguments.Result(new JsonObject { ["type"] = type, ["id"] = id, ["deleted"] = true });
    }

    private async Task<McpToolResult> GetSingletonAsync(SchemaType type, JsonElement arguments, bool drafts, CancellationToken cancellationToken)
    {
        McpArguments.Only(arguments, McpQueryArguments.Single);
        var query = WithStatus(format.ParseQuery(McpQueryArguments.Parameters(arguments, McpQueryArguments.Single)), drafts);
        var entries = await content.QueryAsync(type.Name, query with { Page = new PageRequest { Size = 1, WithTotal = false } }, cancellationToken);
        return entries.Items.Count > 0
            ? McpArguments.Result(format.Entry(entries.Items[0]))
            : McpToolResult.Error(McpErrorCodes.SingletonEmpty, $"The {type.Name} singleton has no entry{(query.Status == QueryStatus.Published ? " that is published" : "")}.");
    }

    private async Task<McpToolResult> UpdateSingletonAsync(SchemaType type, JsonElement arguments, CancellationToken cancellationToken)
    {
        McpArguments.Only(arguments, "expectedVersion", "fields");
        var expected = McpArguments.Integer(arguments, "expectedVersion") ?? throw McpArguments.Required("expectedVersion");
        var fields = McpArguments.Document(McpArguments.Object(arguments, "fields") ?? throw McpArguments.Required("fields"));
        if (await SingletonIdAsync(type, cancellationToken) is { } id)
        {
            return McpArguments.Result(format.Entry(await content.PatchAsync(type.Name, id, fields, expected, cancellationToken)));
        }

        return expected == 0
            ? McpArguments.Result(format.Entry(await content.CreateAsync(type.Name, fields, cancellationToken)))
            : McpToolResult.Error(McpErrorCodes.SingletonEmpty, $"The {type.Name} singleton has no entry; send expectedVersion 0 to create it.");
    }

    private async Task<McpToolResult> DiscardSingletonAsync(SchemaType type, JsonElement arguments, CancellationToken cancellationToken)
    {
        McpArguments.Only(arguments, "expectedVersion", "locale");
        return await SingletonIdAsync(type, cancellationToken) is { } id
            ? await DiscardAsync(type.Name, id, arguments, ["expectedVersion", "locale"], cancellationToken)
            : McpToolResult.Error(McpErrorCodes.SingletonEmpty, $"The {type.Name} singleton has no entry.");
    }

    // The id of the singleton's entry as the caller reads its draft, which a write needs.
    private async Task<Guid?> SingletonIdAsync(SchemaType type, CancellationToken cancellationToken)
    {
        var query = new ContentQuery { Status = QueryStatus.Draft, Select = ["id"], Page = new PageRequest { Size = 1, WithTotal = false } };
        var entries = await content.QueryAsync(type.Name, query, cancellationToken);
        return entries.Items.Count > 0 ? entries.Items[0].Get<Guid>("id") : null;
    }

    private async Task<McpToolResult> PublishAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        McpArguments.Only(arguments, "type", "id", "expectedVersion", "locale");
        if (await AvailableAsync(arguments, cancellationToken) is not { } type)
        {
            return NotAvailable(arguments);
        }

        var options = new LocaleWriteOptions { Locale = McpArguments.Text(arguments, "locale"), ExpectedVersion = McpArguments.Integer(arguments, "expectedVersion") };
        return McpArguments.Result(format.Published(await content.PublishAsync(type.Name, McpArguments.Id(arguments), options, cancellationToken)));
    }

    private async Task<McpToolResult> UnpublishAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        McpArguments.Only(arguments, "type", "id", "locale");
        if (await AvailableAsync(arguments, cancellationToken) is not { } type)
        {
            return NotAvailable(arguments);
        }

        var options = new LocaleOptions { Locale = McpArguments.Text(arguments, "locale") };
        return McpArguments.Result(format.Entry(await content.UnpublishAsync(type.Name, McpArguments.Id(arguments), options, cancellationToken)));
    }

    private async Task<McpToolResult> ScheduleAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        McpArguments.Only(arguments, "type", "id", "action", "at", "timeZone", "locale");
        if (await AvailableAsync(arguments, cancellationToken) is not { } type)
        {
            return NotAvailable(arguments);
        }

        // The body of the Management API's schedule route is the three keys, read by the code that reads it there.
        JsonObject body = [];
        foreach (var name in (ReadOnlySpan<string>)["action", "at", "timeZone"])
        {
            if (arguments.TryGetProperty(name, out var value))
            {
                body[name] = JsonNode.Parse(value.GetRawText());
            }
        }

        var id = McpArguments.Id(arguments);
        var schedule = format.ParseSchedule(McpArguments.Element(body));
        var options = new LocaleOptions { Locale = McpArguments.Text(arguments, "locale") };
        return McpArguments.Result(format.Schedule(await content.ScheduleAsync(type.Name, id, schedule, options, cancellationToken)));
    }

    private async Task<McpToolResult> QueryAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        McpArguments.Only(arguments, ["type", McpQueryArguments.Search, .. McpQueryArguments.List]);
        var described = await access.ContentAsync(cancellationToken);
        if (Available(described, arguments) is not { } type)
        {
            return NotAvailable(arguments);
        }

        string[] names = [McpQueryArguments.Search, .. McpQueryArguments.List];
        var query = format.ParseQuery(McpQueryArguments.Parameters(arguments, names));
        return McpArguments.Result(format.List(await content.QueryAsync(type.Name, WithStatus(query, described.Types[type.Name].Drafts), cancellationToken)));
    }

    // The type a shared tool names, when it is exposed and the caller may read it. One that is not is answered alike
    // whether it exists or not.
    private async Task<SchemaType?> AvailableAsync(JsonElement arguments, CancellationToken cancellationToken) =>
        Available(await access.ContentAsync(cancellationToken), arguments);

    private SchemaType? Available(ContentAccess described, JsonElement arguments)
    {
        var name = McpArguments.RequiredText(arguments, "type");
        return IsExposed(name) ? described.Schema.FindContentType(name) : null;
    }

    private static McpToolResult NotAvailable(JsonElement arguments) =>
        McpToolResult.Error(McpErrorCodes.TypeNotAvailable, $"No content type '{McpArguments.Text(arguments, "type")}' is available to the caller.");

    private bool IsExposed(string type) =>
        options.Value.ExposedTypes is not { Count: > 0 } named || named.Contains(type, StringComparer.Ordinal);

    // The draft is read by default, as the Management API reads it, by a caller that may read drafts.
    private static ContentQuery WithStatus(ContentQuery query, bool drafts) =>
        query with { Status = query.Status ?? (drafts ? QueryStatus.Draft : QueryStatus.Published) };

    private static GetOptions OptionsOf(ContentQuery query) => new()
    {
        Status = query.Status ?? QueryStatus.Published,
        Locale = query.Locale,
        Fallback = query.Fallback,
        Select = query.Select,
        Populate = query.Populate,
        PopulateDepth = query.PopulateDepth,
        RichTextFormat = query.RichTextFormat,
    };

    // "article_create_draft" as ("article", "create_draft"). No verb ends with another, so at most one fits.
    private static (string Type, string Verb)? Split(string name)
    {
        foreach (var verb in Verbs)
        {
            if (name.Length > verb.Length + 1 && name.EndsWith(verb, StringComparison.Ordinal) && name[^(verb.Length + 1)] == '_')
            {
                return (name[..^(verb.Length + 1)], verb);
            }
        }

        return null;
    }
}
