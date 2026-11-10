using System.Text.Json;
using System.Text.Json.Nodes;
using FluentCMS.Agents;
using FluentCMS.Authorization;
using FluentCMS.Content;
using FluentCMS.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FluentCMS.Plugins.Mcp;

// The media tools of spec 5.4, over IMediaService. They exist only where a media service is registered and the media
// plugin is enabled for the site of the call; elsewhere the names are no tools at all. The list holds a tool when core
// says a grant of the caller gives its permission, and the service checks every call. The answers are the JSON of the
// media routes of the Management API.
internal sealed class McpMediaTools(
    IServiceProvider services,
    IEffectiveAccess access,
    IPluginState plugins,
    McpDownloads downloads,
    ILogger<McpMediaTools> logger) : IMcpToolProvider
{
    public const string MediaPlugin = "media";

    public const string List = "media_list";
    public const string Search = "media_search";
    public const string Get = "media_get";
    public const string Upload = "media_upload";
    public const string Update = "media_update";

    // The permissions of the media plugin, as IMediaService names them.
    private const string ReadPermission = "media.read";
    private const string UploadPermission = "media.upload";
    private const string UpdatePermission = "media.update";

    private const string RootFolder = "root";

    private static readonly string[] ListArguments = ["folder", "subfolders", "type", "tag", "page", "pageSize"];
    private static readonly string[] UpdateArguments = ["title", "alt", "caption", "credit", "tags", "focalPoint", "isPrivate"];

    private static readonly McpToolAnnotations Reads = new() { ReadOnly = true, Idempotent = true };

    private static readonly JsonElement ListSchema = Schema(ListProperties(), []);
    private static readonly JsonElement SearchSchema = Schema(ListProperties(new() { ["q"] = Text("The text to look for in the title, the file name and the tags, 1 to 256 characters.") }), ["q"]);
    private static readonly JsonElement GetSchema = Schema(new() { ["id"] = Id("The id of the asset.") }, ["id"]);

    private static readonly JsonElement UploadSchema = Schema(
        new()
        {
            ["url"] = Text("An https URL of a public host to fetch the file from. Either url or data."),
            ["data"] = Text("The bytes of the file in base64. Either url or data."),
            ["fileName"] = Text("The name of the file, such as harbour.jpg. Required with data; with url it defaults to the last part of its path."),
            ["folderId"] = Id("The folder to store the asset in; the root when left out."),
        },
        []);

    private static readonly JsonElement UpdateSchema = Schema(
        new()
        {
            ["id"] = Id("The id of the asset."),
            ["title"] = Text("The title."),
            ["alt"] = Text("The alternative text, in the locale of the connection."),
            ["caption"] = Text("The caption, in the locale of the connection."),
            ["credit"] = Text("The credit."),
            ["tags"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject { ["type"] = "string" }, ["description"] = "The tags, which replace the ones the asset has." },
            ["focalPoint"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["x"] = new JsonObject { ["type"] = "number", ["minimum"] = 0, ["maximum"] = 1 },
                    ["y"] = new JsonObject { ["type"] = "number", ["minimum"] = 0, ["maximum"] = 1 },
                },
                ["required"] = new JsonArray("x", "y"),
                ["description"] = "The point of an image that a crop keeps, from 0 to 1 on each axis.",
            },
            ["isPrivate"] = new JsonObject { ["type"] = "boolean", ["description"] = "Whether the file is served only through a signed download URL." },
        },
        ["id"]);

    public async ValueTask<IReadOnlyList<McpToolDescriptor>> ListToolsAsync(McpListRequest request, CancellationToken cancellationToken = default)
    {
        if (await MediaAsync(request.Operation, cancellationToken) is null)
        {
            return [];
        }

        var held = await access.HeldAsync([ReadPermission, UploadPermission, UpdatePermission], cancellationToken);
        List<McpToolDescriptor> tools = [];
        if (held.Contains(ReadPermission))
        {
            tools.Add(new McpToolDescriptor(List, "Lists the media assets of the site, newest first, by folder, type and tag.", ListSchema) { Title = "List media", Annotations = Reads });
            tools.Add(new McpToolDescriptor(Search, "Finds media assets whose title, file name or tags contain a text.", SearchSchema) { Title = "Search media", Annotations = Reads });
            tools.Add(new McpToolDescriptor(Get, "Reads a media asset by its id.", GetSchema) { Title = "Get media", Annotations = Reads });
        }

        if (held.Contains(UploadPermission))
        {
            tools.Add(new McpToolDescriptor(Upload, "Stores a file as a new media asset, from an https URL of a public host or from base64. The type is read from the bytes, and the upload limits of the site apply.", UploadSchema)
            {
                Title = "Upload media",
                Annotations = new McpToolAnnotations { Destructive = false, OpenWorld = true },
            });
        }

        if (held.Contains(UpdatePermission))
        {
            tools.Add(new McpToolDescriptor(Update, "Changes the metadata of a media asset. What is left out stays.", UpdateSchema)
            {
                Title = "Update media",
                Annotations = new McpToolAnnotations { Idempotent = true },
            });
        }

        return tools;
    }

    public async ValueTask<McpToolResult?> CallToolAsync(McpToolCall request, CancellationToken cancellationToken = default)
    {
        if (request.Name is not (List or Search or Get or Upload or Update) || await MediaAsync(request.Operation, cancellationToken) is not { } media)
        {
            return null;
        }

        var arguments = request.Arguments;
        switch (request.Name)
        {
            case List:
                McpArguments.Only(arguments, ListArguments);
                return Page(await media.ListAsync(QueryOf(arguments, search: null), cancellationToken));

            case Search:
                McpArguments.Only(arguments, ["q", .. ListArguments]);
                return Page(await media.ListAsync(QueryOf(arguments, McpArguments.RequiredText(arguments, "q")), cancellationToken));

            case Get:
                McpArguments.Only(arguments, "id");
                return McpArguments.Result(Asset(await media.GetAsync(McpArguments.Id(arguments), cancellationToken)));

            case Upload:
                return await UploadAsync(media, arguments, cancellationToken);

            default:
                McpArguments.Only(arguments, ["id", .. UpdateArguments]);
                return McpArguments.Result(Asset(await media.UpdateAsync(McpArguments.Id(arguments), ChangesOf(arguments), cancellationToken)));
        }
    }

    // The media plugin references nothing of this one, so its service is asked for by its contract, and may be absent.
    private async ValueTask<IMediaService?> MediaAsync(OperationContext operation, CancellationToken cancellationToken) =>
        services.GetService<IMediaService>() is { } media && await plugins.IsEnabledAsync(MediaPlugin, operation.Site, cancellationToken) ? media : null;

    private async Task<McpToolResult> UploadAsync(IMediaService media, JsonElement arguments, CancellationToken cancellationToken)
    {
        McpArguments.Only(arguments, "url", "data", "fileName", "folderId");
        var url = McpArguments.Text(arguments, "url");
        var data = McpArguments.Text(arguments, "data");
        var fileName = McpArguments.Text(arguments, "fileName");
        var folderId = McpArguments.OptionalId(arguments, "folderId");
        if (url is null == data is null)
        {
            throw McpArguments.Invalid(url is null ? "url" : "data", "An upload has a url or data, and not both.");
        }

        Stream content;
        var host = Uri.TryCreate(url, UriKind.Absolute, out var named) ? named.Host : "";
        try
        {
            if (url is not null)
            {
                var source = McpDownloads.Checked(url);
                fileName ??= Uri.UnescapeDataString(source.Segments[^1]).Trim('/') is { Length: > 0 } last ? last : "download";
                content = downloads.Open(source, cancellationToken);
            }
            else
            {
                content = new MemoryStream(Convert.FromBase64String(data!), writable: false);
            }
        }
        catch (FormatException)
        {
            throw McpArguments.Invalid("data", "'data' is the file in base64.");
        }
        catch (McpDownloadException exception)
        {
            return Refusal(exception, host);
        }

        await using (content)
        {
            try
            {
                var created = await media.UploadAsync(new MediaUpload(folderId, [new MediaUploadFile(fileName ?? throw McpArguments.Required("fileName"), content)]), cancellationToken);
                return McpArguments.Result(new JsonObject { ["items"] = new JsonArray([.. created.Select(asset => (JsonNode)Asset(asset))]) });
            }
            catch (McpDownloadException exception)
            {
                return Refusal(exception, host);
            }
        }
    }

    // The log names the host and never the URL, whose path and query may hold a secret.
    private McpToolResult Refusal(McpDownloadException exception, string host)
    {
        McpLog.UploadSourceRefused(logger, host, exception.Message);
        return McpToolResult.Error(exception.Code, exception.Message);
    }

    private static MediaQuery QueryOf(JsonElement arguments, string? search)
    {
        var folder = McpArguments.Text(arguments, "folder");
        return new MediaQuery
        {
            FolderId = folder is null or RootFolder ? null : Guid.TryParseExact(folder, "D", out var id) ? id : throw McpArguments.Invalid("folder", $"A folder is an id or '{RootFolder}'."),
            RootOnly = folder == RootFolder,
            IncludeSubfolders = McpArguments.Flag(arguments, "subfolders") ?? false,
            MimeType = McpArguments.Text(arguments, "type"),
            Tag = McpArguments.Text(arguments, "tag"),
            Search = search,
            Page = McpArguments.Integer(arguments, "page") ?? 1,
            PageSize = McpArguments.Integer(arguments, "pageSize") ?? 50,
        };
    }

    // Read as the PATCH of the Management API binds its body.
    private static MediaAssetChanges ChangesOf(JsonElement arguments)
    {
        JsonObject changes = [];
        foreach (var property in arguments.EnumerateObject().Where(property => UpdateArguments.Contains(property.Name, StringComparer.Ordinal)))
        {
            changes[property.Name] = JsonNode.Parse(property.Value.GetRawText());
        }

        try
        {
            return changes.Deserialize<MediaAssetChanges>(JsonSerializerOptions.Web) ?? new MediaAssetChanges();
        }
        catch (JsonException exception)
        {
            throw McpArguments.Invalid(exception.Path is { Length: > 2 } path ? path[2..] : "", "The changes are not what an asset takes: text for the title, alt, caption and credit, an array of text for the tags, x and y for the focal point.");
        }
    }

    private static McpToolResult Page(PagedResult<MediaAsset> page) => McpArguments.Result(new JsonObject
    {
        ["items"] = new JsonArray([.. page.Items.Select(asset => (JsonNode)Asset(asset))]),
        ["total"] = page.Total,
        ["page"] = page.Page,
        ["pageSize"] = page.PageSize,
    });

    private static JsonObject Asset(MediaAsset asset) => (JsonObject)JsonSerializer.SerializeToNode(asset, JsonSerializerOptions.Web)!;

    private static JsonObject ListProperties(JsonObject? first = null)
    {
        var properties = first ?? [];
        properties["folder"] = Text($"The id of a folder, or '{RootFolder}' for the assets outside every folder; every folder when left out.");
        properties["subfolders"] = new JsonObject { ["type"] = "boolean", ["description"] = "Whether a folder takes in the assets of its subfolders." };
        properties["type"] = Text("A MIME type, such as image/png, or a family, such as image/*.");
        properties["tag"] = Text("A tag the assets carry.");
        properties["page"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["description"] = "The page, from 1." };
        properties["pageSize"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 100, ["description"] = "The assets on a page, 50 when left out." };
        return properties;
    }

    private static JsonElement Schema(JsonObject properties, string[] required)
    {
        JsonObject schema = new() { ["type"] = "object", ["properties"] = properties };
        if (required.Length > 0)
        {
            schema["required"] = new JsonArray([.. required.Select(name => (JsonNode)name)]);
        }

        schema["additionalProperties"] = false;
        return McpArguments.Element(schema);
    }

    private static JsonObject Text(string description) => new() { ["type"] = "string", ["description"] = description };

    private static JsonObject Id(string description) => new() { ["type"] = "string", ["format"] = "uuid", ["description"] = description };
}
