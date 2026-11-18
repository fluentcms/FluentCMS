using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using AngleSharp.Dom;
using Bunit;
using FluentCMS.Admin;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Components;
using FluentCMS.Admin.Configuration;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Plugins.Media.Admin.Tests;

// The half's components with the admin's services they inject: a media library in memory behind IManagementApi, the
// caller's permissions, and an announcer that keeps what it was given.
public abstract class MediaTestContext : BunitContext
{
    protected MediaTestContext()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddCmsComponents();
        Services.AddLocalization();
        Services.AddSingleton<IManagementApi>(Library);
        Services.AddSingleton<IAdminContext>(Admin);
        Services.AddSingleton<IAnnouncer>(Announcer);
        Services.AddSingleton(new AdminConfiguration { ApiBaseUrl = new Uri("https://cms.example.com/") });
    }

    internal FakeMediaLibrary Library { get; } = new();

    internal FakeAdminContext Admin { get; } = new();

    internal FakeAnnouncer Announcer { get; } = new();

    protected static IElement Button<T>(IRenderedComponent<T> cut, string text)
        where T : IComponent =>
        cut.FindAll("fluent-button").First(button => button.TextContent.Trim() == text);

    protected static bool HasButton<T>(IRenderedComponent<T> cut, string text)
        where T : IComponent =>
        cut.FindAll("fluent-button").Any(button => button.TextContent.Trim() == text);

    protected static IElement Field<T>(IRenderedComponent<T> cut, string label)
        where T : IComponent
    {
        var found = cut.FindAll("label").First(element => element.TextContent.Trim().TrimEnd('*').TrimEnd() == label);
        return cut.Find("#" + found.GetAttribute("for"));
    }
}

internal sealed class FakeAnnouncer : IAnnouncer
{
    public List<string> Messages { get; } = [];

    public void Announce(string message, AnnouncementPoliteness politeness = AnnouncementPoliteness.Polite) => Messages.Add(message);
}

internal sealed class FakeAdminContext : IAdminContext, IAdminAccess
{
    public HashSet<string> Permissions { get; } = new(StringComparer.Ordinal) { "media.read" };

    public MeResponse? User => null;

    public MySiteInfo? Site { get; set; }

    public string? ContentLocale => "en";

    public IAdminAccess Access => this;

    public event Action? Changed;

    public bool Allows(string permission) => Permissions.Contains(permission);

    public TypeAccessInfo? ForType(string type) => null;

    public void Grant(params string[] permissions) => Permissions.UnionWith(permissions);

    public void RaiseChanged() => Changed?.Invoke();
}

// GET and POST /media, the moves and the folders, as the server answers them, over assets and folders in memory. An
// upload refuses a name ending in .exe (FCMS1583), .svg (FCMS1584) or .txt (FCMS1581).
internal sealed class FakeMediaLibrary : IManagementApi
{
    public List<JsonObject> Assets { get; } = [];

    public List<JsonObject> Folders { get; } = [];

    public List<ApiRequest> Requests { get; } = [];

    // Each file uploaded, with its name and its bytes as Latin-1 text.
    public List<(string FileName, string Bytes)> Uploads { get; } = [];

    // Runs before each answer, so that a test can hold one back.
    public Func<ApiRequest, Task>? BeforeAnswer { get; set; }

    // Refuses a move of this asset.
    public Guid? RefuseMoveOf { get; set; }

    // Answers the next GET /media with this problem instead.
    public ApiProblem? ListProblem { get; set; }

    public Guid AddFolder(string name, Guid? parent = null)
    {
        var id = Guid.NewGuid();
        Folders.Add(new JsonObject { ["id"] = id, ["siteId"] = Guid.Empty, ["parentId"] = parent, ["name"] = name });
        return id;
    }

    public Guid AddAsset(string fileName, Guid? folder = null, string mimeType = "image/png", string? title = null, params string[] tags)
    {
        var id = Guid.NewGuid();
        Assets.Add(new JsonObject
        {
            ["id"] = id,
            ["folderId"] = folder,
            ["fileName"] = fileName,
            ["mimeType"] = mimeType,
            ["size"] = 2048,
            ["width"] = mimeType.StartsWith("image/", StringComparison.Ordinal) ? 640 : null,
            ["height"] = mimeType.StartsWith("image/", StringComparison.Ordinal) ? 480 : null,
            ["title"] = title,
            ["tags"] = new JsonArray([.. tags.Select(tag => (JsonNode)JsonValue.Create(tag)!)]),
            ["urls"] = new JsonObject
            {
                ["original"] = $"/media/{id:D}/original/{fileName}",
                ["thumbnail"] = mimeType.StartsWith("image/", StringComparison.Ordinal) ? $"/media/{id:D}/thumbnail/{fileName}" : null,
            },
        });
        return id;
    }

    public Guid? FolderOf(Guid asset) => (Guid?)Assets.Single(item => (Guid)item["id"]! == asset)["folderId"];

    public async Task<ApiResponse<T>> SendAsync<T>(ApiRequest request, JsonTypeInfo<T> answer, CancellationToken cancellationToken)
    {
        await Task.Yield();
        if (BeforeAnswer is { } before)
        {
            await before(request);
        }

        var node = Answer(request);
        return new ApiResponse<T>(HttpStatusCode.OK, null, node.Deserialize(answer)!);
    }

    public async Task<ApiResponse> SendAsync(ApiRequest request, CancellationToken cancellationToken)
    {
        await Task.Yield();
        if (BeforeAnswer is { } before)
        {
            await before(request);
        }

        Answer(request);
        return new ApiResponse(HttpStatusCode.NoContent, null);
    }

    private JsonObject Answer(ApiRequest request)
    {
        Requests.Add(request);
        var (path, query) = request.Path.Split('?', 2) is [var p, var q] ? (p, Query(q)) : (request.Path, new Dictionary<string, string>());
        var segments = path.Split('/');
        return (request.Method.Method, segments) switch
        {
            ("GET", ["media"]) => List(query),
            ("GET", ["media", "folders"]) => new JsonObject { ["items"] = new JsonArray([.. Folders.Select(folder => folder.DeepClone())]) },
            ("POST", ["media"]) => Upload(request.Content!),
            ("POST", ["media", var id, "move"]) => MoveAsset(Guid.Parse(id), Id(request.Body!["folderId"])),
            ("POST", ["media", "folders"]) => CreateFolder(request.Body!["name"]!.GetValue<string>(), Id(request.Body["parentId"])),
            ("PATCH", ["media", "folders", var id]) => Folder(Guid.Parse(id), folder => folder["name"] = request.Body!["name"]!.GetValue<string>()),
            ("POST", ["media", "folders", var id, "move"]) => Folder(Guid.Parse(id), folder => folder["parentId"] = Id(request.Body!["parentId"])),
            ("DELETE", ["media", "folders", var id]) => DeleteFolder(Guid.Parse(id)),
            _ => throw new InvalidOperationException($"No answer for {request.Method} {request.Path}."),
        };
    }

    private static Guid? Id(JsonNode? node) => node is null ? null : Guid.Parse(node.GetValue<string>());

    private static Dictionary<string, string> Query(string query) =>
        query.Split('&').Select(part => part.Split('=', 2)).ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1]), StringComparer.Ordinal);

    private JsonObject List(Dictionary<string, string> query)
    {
        if (ListProblem is { } problem)
        {
            ListProblem = null;
            throw new ApiProblemException(problem);
        }

        IEnumerable<JsonObject> found = Assets;
        if (query.TryGetValue("folder", out var folder))
        {
            var root = folder == "root";
            var reach = root ? null : (query.ContainsKey("subfolders") ? Below(Guid.Parse(folder)) : [Guid.Parse(folder)]);
            found = found.Where(asset => (Guid?)asset["folderId"] is { } id ? reach?.Contains(id) == true : root);
        }

        if (query.TryGetValue("search", out var search))
        {
            found = found.Where(asset => asset["fileName"]!.GetValue<string>().Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        if (query.TryGetValue("type", out var type))
        {
            found = found.Where(asset => type.EndsWith("/*", StringComparison.Ordinal)
                ? asset["mimeType"]!.GetValue<string>().StartsWith(type[..^1], StringComparison.Ordinal)
                : asset["mimeType"]!.GetValue<string>() == type);
        }

        if (query.TryGetValue("tag", out var tag))
        {
            found = found.Where(asset => asset["tags"]!.AsArray().Any(item => item!.GetValue<string>() == tag));
        }

        var all = found.ToList();
        var page = int.Parse(query["page"], CultureInfo.InvariantCulture);
        var size = int.Parse(query["pageSize"], CultureInfo.InvariantCulture);
        return new JsonObject
        {
            ["items"] = new JsonArray([.. all.Skip((page - 1) * size).Take(size).Select(asset => asset.DeepClone())]),
            ["total"] = all.Count,
            ["page"] = page,
            ["pageSize"] = size,
        };
    }

    private HashSet<Guid> Below(Guid folder)
    {
        HashSet<Guid> found = [folder];
        while (Folders.Where(item => (Guid?)item["parentId"] is { } parent && found.Contains(parent)).Select(item => (Guid)item["id"]!).Where(found.Add).ToList() is { Count: > 0 })
        {
        }

        return found;
    }

    private const string Crlf = "\r\n";

    // The body as the half writes it: an optional folderId part and one file part, whose name is in filename*.
    private JsonObject Upload(ApiContent content)
    {
        var boundary = content.MediaType.Split("boundary=", 2)[1];
        var text = System.Text.Encoding.Latin1.GetString(content.Bytes.Span);
        var parts = text.Split("--" + boundary, StringSplitOptions.None)[1..^1];
        var files = parts.Where(part => part.Contains("name=\"files\"", StringComparison.Ordinal)).ToList();
        if (files.Count != 1 || !text.EndsWith("--" + boundary + "--" + Crlf, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"An upload of the library sends one file part in a closed body, not {files.Count}.");
        }

        var encoded = System.Text.RegularExpressions.Regex.Match(files[0], @"filename\*=UTF-8''([^\r\n;]+)").Groups[1].Value;
        var file = (FileName: Uri.UnescapeDataString(encoded), Bytes: files[0][(files[0].IndexOf(Crlf + Crlf, StringComparison.Ordinal) + 4)..^2]);
        Uploads.Add(file);
        var code = Path.GetExtension(file.FileName) switch
        {
            ".exe" => "FCMS1583",
            ".svg" => "FCMS1584",
            ".txt" => "FCMS1581",
            _ => null,
        };
        if (code is not null)
        {
            throw new ApiProblemException(new ApiProblem { Status = 400, Code = code, Detail = $"The file '{file.FileName}' is refused." });
        }

        var folderPart = parts.SingleOrDefault(part => part.Contains("name=\"folderId\"", StringComparison.Ordinal));
        Guid? folder = folderPart is null ? null : Guid.Parse(folderPart.Split(Crlf + Crlf, 2)[1].Trim());
        var id = AddAsset(file.FileName, folder);
        return new JsonObject { ["items"] = new JsonArray(new JsonObject { ["id"] = id, ["folderId"] = folder, ["fileName"] = file.FileName }) };
    }

    private JsonObject MoveAsset(Guid id, Guid? folder)
    {
        if (id == RefuseMoveOf)
        {
            throw new ApiProblemException(new ApiProblem { Status = 403, Code = "FCMS1100" });
        }

        var asset = Assets.Single(item => (Guid)item["id"]! == id);
        asset["folderId"] = folder;
        return new JsonObject { ["id"] = id, ["folderId"] = folder, ["fileName"] = asset["fileName"]!.DeepClone() };
    }

    private JsonObject CreateFolder(string name, Guid? parent)
    {
        if (Folders.Any(folder => folder["name"]!.GetValue<string>() == name && (Guid?)folder["parentId"] == parent))
        {
            throw new ApiProblemException(new ApiProblem { Status = 409, Code = "FCMS1587" });
        }

        var id = AddFolder(name, parent);
        return (JsonObject)Folders.Single(folder => (Guid)folder["id"]! == id).DeepClone();
    }

    private JsonObject Folder(Guid id, Action<JsonObject> change)
    {
        var folder = Folders.Single(item => (Guid)item["id"]! == id);
        change(folder);
        return (JsonObject)folder.DeepClone();
    }

    private JsonObject DeleteFolder(Guid id)
    {
        if (Assets.Any(asset => (Guid?)asset["folderId"] == id) || Folders.Any(folder => (Guid?)folder["parentId"] == id))
        {
            throw new ApiProblemException(new ApiProblem { Status = 409, Code = "FCMS1588" });
        }

        Folders.RemoveAll(folder => (Guid)folder["id"]! == id);
        return [];
    }
}

