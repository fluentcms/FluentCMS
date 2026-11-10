using System.Text.Json;
using FluentCMS.Agents;
using FluentCMS.Authorization;
using FluentCMS.Content;

namespace FluentCMS.Plugins.Sample;

// The tools the sample offers to the MCP server. Each reaches content through the services, which decide what the caller
// may do: a refusal is left to the server, which answers it with the code of the REST APIs.
public sealed class SampleTools(IContentService content, IPermissionChecker permissions, PluginDbContext<SamplePlugin> database, TimeProvider time) : IMcpToolProvider
{
    public const string WhoAmI = "sample_whoami";
    public const string EntryCreate = "sample_entry_create";
    public const string EntryTitles = "sample_entry_titles";
    public const string NoteAdd = "sample_note_add";

    private static readonly JsonElement NoArguments = Schema("""{ "type": "object", "properties": {} }""");

    private static readonly JsonElement EntryArguments = Schema(
        """{ "type": "object", "properties": { "type": { "type": "string" }, "title": { "type": "string" } }, "required": ["type", "title"] }""");

    private static readonly JsonElement TitlesArguments = Schema(
        """{ "type": "object", "properties": { "type": { "type": "string" }, "status": { "type": "string", "enum": ["published", "draft"] } }, "required": ["type"] }""");

    private static readonly JsonElement NoteArguments = Schema(
        """{ "type": "object", "properties": { "text": { "type": "string" } }, "required": ["text"] }""");

    private static readonly JsonElement WhoAmIResult = Schema(
        """{ "type": "object", "properties": { "principal": { "type": "string" }, "kind": { "type": "string" }, "origin": { "type": "string" }, "clientId": { "type": ["string", "null"] }, "site": { "type": "string" }, "locale": { "type": "string" } } }""");

    // The note tool is listed only for a caller who may manage notes, as a tool list is filtered by what the caller may do.
    public async ValueTask<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(McpListRequest request, CancellationToken cancellationToken = default)
    {
        List<McpToolDescriptor> tools =
        [
            new(WhoAmI, "Says who the caller is and where the call runs.", NoArguments)
            {
                Title = "Who am I",
                OutputSchema = WhoAmIResult,
                Annotations = new McpToolAnnotations { ReadOnly = true, Idempotent = true },
            },
            new(EntryCreate, "Creates a draft of a content type with a title.", EntryArguments) { Annotations = new McpToolAnnotations { Destructive = false } },
            new(EntryTitles, "Lists the titles of the entries of a content type that the caller may read.", TitlesArguments) { Annotations = new McpToolAnnotations { ReadOnly = true } },
        ];

        if (await permissions.AllowsAsync(SamplePermissions.NotesManage, request.Operation.Site, cancellationToken))
        {
            tools.Add(new McpToolDescriptor(NoteAdd, "Adds a sample note.", NoteArguments) { Annotations = new McpToolAnnotations { Destructive = false } });
        }

        return tools;
    }

    public async ValueTask<McpToolResult?> CallToolAsync(McpToolCall request, CancellationToken cancellationToken = default)
    {
        var arguments = request.Arguments;
        switch (request.Name)
        {
            case WhoAmI:
                var operation = request.Operation;
                return McpToolResult.FromJson(JsonSerializer.SerializeToElement(new
                {
                    principal = operation.Principal.Id,
                    kind = operation.Principal.Kind.ToString(),
                    origin = operation.Origin.ToString(),
                    clientId = operation.ClientId,
                    site = operation.Site.Value,
                    locale = operation.Locale,
                }));

            case EntryCreate:
                var created = await content.CreateAsync(Text(arguments, "type"), new ContentDocument { ["title"] = Text(arguments, "title") }, cancellationToken);
                return McpToolResult.FromJson(JsonSerializer.SerializeToElement(new { id = created.Get<Guid>("id") }));

            case EntryTitles:
                var query = new ContentQuery { Status = Text(arguments, "status") == "draft" ? QueryStatus.Draft : QueryStatus.Published };
                var entries = await content.QueryAsync(Text(arguments, "type"), query, cancellationToken);
                return McpToolResult.FromText(string.Join('\n', entries.Items.Select(entry => entry.Get<string>("title"))));

            case NoteAdd:
                await permissions.RequireAsync(SamplePermissions.NotesManage, request.Operation.Site, cancellationToken);
                var text = Text(arguments, "text");
                if (text.Length == 0)
                {
                    return McpToolResult.Error(SamplePlugin.NoteExists, "A note needs a text.");
                }

                database.Add(new SampleNote { Id = Guid.CreateVersion7(), Text = text, CreatedAt = time.GetUtcNow() });
                await database.SaveChangesAsync(cancellationToken);
                return McpToolResult.FromText($"Added the note '{text}'.");

            default:
                return null;
        }
    }

    private static string Text(JsonElement arguments, string name) =>
        arguments.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";

    private static JsonElement Schema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

// A fixed resource and a template, which the provider matches itself.
public sealed class SampleResources(SampleGreeting greeting) : IMcpResourceProvider
{
    public const string Greeting = "sample://greeting";
    public const string EchoTemplate = "sample://echo/{text}";

    private const string EchoPrefix = "sample://echo/";

    public ValueTask<IReadOnlyList<McpResourceDescriptor>> ListResourcesAsync(McpListRequest request, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<McpResourceDescriptor>>(
        [
            new(Greeting, "greeting") { Description = "The greeting of the sample plugin.", MimeType = "text/plain" },
            new(EchoTemplate, "echo") { Description = "The text the URI names.", MimeType = "text/plain" },
        ]);

    public ValueTask<IReadOnlyList<McpResourceContent>?> ReadResourceAsync(McpResourceRead request, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<McpResourceContent>?>(request.Uri switch
        {
            Greeting => [McpResourceContent.FromText(request.Uri, greeting.Text)],
            var uri when uri.StartsWith(EchoPrefix, StringComparison.Ordinal) => [McpResourceContent.FromText(uri, Uri.UnescapeDataString(uri[EchoPrefix.Length..]))],
            _ => null,
        });
}

public sealed class SamplePrompts(SampleGreeting greeting) : IMcpPromptProvider
{
    public const string Greet = "sample_greet";

    public ValueTask<IReadOnlyList<McpPromptDescriptor>> ListPromptsAsync(McpListRequest request, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<McpPromptDescriptor>>(
        [
            new(Greet)
            {
                Description = "Greets someone as the sample plugin does.",
                Arguments = [new McpPromptArgument("name") { Description = "Who to greet.", Required = true }],
            },
        ]);

    public ValueTask<McpPromptResult?> GetPromptAsync(McpPromptGet request, CancellationToken cancellationToken = default)
    {
        if (request.Name != Greet)
        {
            return ValueTask.FromResult<McpPromptResult?>(null);
        }

        // A missing argument is the caller's mistake, which the server answers with the plugin's code.
        if (!request.Arguments.TryGetValue("name", out var name) || name.Length == 0)
        {
            throw new PluginException(SamplePlugin.NoteExists, "The prompt needs a name.");
        }

        return ValueTask.FromResult<McpPromptResult?>(new McpPromptResult([new McpPromptMessage(McpPromptRole.User, $"Greet {name} with: {greeting.Text}")])
        {
            Description = "A greeting.",
        });
    }
}
