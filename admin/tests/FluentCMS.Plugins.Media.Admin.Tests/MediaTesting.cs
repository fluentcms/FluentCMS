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
using FluentCMS.Admin.Plugins;
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
        Services.AddSingleton<IAdminOverlays>(Overlays);
        Services.AddSingleton<IAdminPluginServices>(provider => new FakePluginServices(provider));
    }

    internal FakeOverlays Overlays { get; } = new();

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

// What the half shows through the shell's overlays, which a test renders with Render(Overlays.Host).
internal sealed class FakeOverlays : IAdminOverlays
{
    public List<RenderFragment> Shown { get; } = [];

    public RenderFragment Host => builder =>
    {
        foreach (var content in Shown)
        {
            builder.AddContent(0, content);
        }
    };

    public IDisposable Show(RenderFragment content)
    {
        Shown.Add(content);
        return new Removal(() => Shown.Remove(content));
    }

    private sealed class Removal(Action remove) : IDisposable
    {
        public void Dispose() => remove();
    }
}

// The half's services as the admin finds them once it has loaded the half: the picker over the test's overlays.
internal sealed class FakePluginServices(IServiceProvider provider) : IAdminPluginServices
{
    public IServiceProvider ProviderOf(Type type) => provider;

    public ValueTask<TService?> FindAsync<TService>(CancellationToken cancellationToken = default)
        where TService : class =>
        ValueTask.FromResult(typeof(TService) == typeof(FluentCMS.Admin.Pickers.IMediaPicker)
            ? (TService)(object)new MediaPicker(provider.GetRequiredService<IAdminOverlays>())
            : null);
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
            ["updatedAt"] = clock,
            ["texts"] = new JsonObject(),
            ["tags"] = new JsonArray([.. tags.Select(tag => (JsonNode)JsonValue.Create(tag)!)]),
            ["urls"] = new JsonObject
            {
                ["original"] = $"/media/{id:D}/original/{fileName}",
                ["thumbnail"] = mimeType.StartsWith("image/", StringComparison.Ordinal) ? $"/media/{id:D}/thumbnail/{fileName}" : null,
            },
        });
        return id;
    }

    // The time of the last change of an asset, which moves on with each one.
    private DateTimeOffset clock = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    // Usage of each asset, as GET /media/{id}/usage answers it.
    public Dictionary<Guid, List<JsonObject>> Usage { get; } = [];

    // Entries of each type by id, with their fields, as a list read answers them.
    public Dictionary<string, List<JsonObject>> Entries { get; } = new(StringComparer.Ordinal);

    public List<JsonObject> Presets { get; } = [];

    // The schema's types: name and title field.
    public List<JsonObject> Types { get; } = [];

    // Answers each PATCH with this problem instead.
    public ApiProblem? PatchProblem { get; set; }

    public JsonObject Asset(Guid id) => Assets.Single(item => (Guid)item["id"]! == id);

    public void AddUsage(Guid asset, string type, Guid entry, string label, string? locale = "en", string field = "image")
    {
        if (!Usage.TryGetValue(asset, out var usage))
        {
            Usage[asset] = usage = [];
        }

        usage.Add(new JsonObject { ["type"] = type, ["entryId"] = entry, ["status"] = "draft", ["locale"] = locale, ["field"] = field });
        if (!Entries.TryGetValue(type, out var entries))
        {
            Entries[type] = entries = [];
            Types.Add(new JsonObject { ["name"] = type, ["kind"] = "collection", ["titleField"] = "title", ["fields"] = new JsonArray() });
        }

        entries.Add(new JsonObject { ["id"] = entry, ["title"] = label });
    }

    // A change made elsewhere, as another tab saves it.
    public void ChangeElsewhere(Guid id, string title)
    {
        var asset = Asset(id);
        asset["title"] = title;
        asset["updatedAt"] = clock = clock.AddMinutes(1);
    }

    public string? Text(Guid id, string locale, string name) => Asset(id)["texts"]![locale]?[name]?.GetValue<string>();

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
            ("GET", ["media", "presets"]) => new JsonObject { ["items"] = new JsonArray([.. Presets.Select(preset => preset.DeepClone())]) },
            ("PUT", ["media", "presets", var name]) => SetPreset(Uri.UnescapeDataString(name), request.Body!.AsObject()),
            ("DELETE", ["media", "presets", var name]) => DeletePreset(Uri.UnescapeDataString(name)),
            ("GET", ["media", var id]) => View(Guid.Parse(id), request.Locale),
            ("PATCH", ["media", var id]) => Patch(Guid.Parse(id), request),
            ("DELETE", ["media", var id]) => DeleteAsset(Guid.Parse(id)),
            ("GET", ["media", var id, "usage"]) => new JsonObject { ["items"] = new JsonArray([.. Usage.GetValueOrDefault(Guid.Parse(id), []).Select(item => item.DeepClone())]) },
            ("POST", ["media", var id, "download-url"]) => new JsonObject { ["url"] = $"/media/{id}/original/file?exp=1&sig=abc", ["expiresAt"] = clock.AddMinutes(5) },
            ("POST", ["media", var id, "replace"]) => Replace(Guid.Parse(id), request.Content!),
            ("GET", ["schema"]) => new JsonObject { ["types"] = new JsonArray([.. Types.Select(type => type.DeepClone())]) },
            ("GET", ["content", var type]) => ListEntries(type, query),
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

    // The asset with its alt text and caption in the locale of the request, or the view's.
    private JsonObject View(Guid id, string? locale)
    {
        var asset = (JsonObject?)Assets.SingleOrDefault(item => (Guid)item["id"]! == id)?.DeepClone()
            ?? throw new ApiProblemException(new ApiProblem { Status = 404, Code = "FCMS1585" });
        var texts = asset["texts"]!.AsObject();
        asset.Remove("texts");
        locale ??= "en";
        asset["locale"] = locale;
        asset["alt"] = texts[locale]?["alt"]?.DeepClone();
        asset["caption"] = texts[locale]?["caption"]?.DeepClone();
        return asset;
    }

    private JsonObject Patch(Guid id, ApiRequest request)
    {
        if (PatchProblem is { } problem)
        {
            throw new ApiProblemException(problem);
        }

        var asset = Asset(id);
        var locale = request.Locale ?? "en";
        foreach (var (name, value) in request.Body!.AsObject())
        {
            if (name is "alt" or "caption")
            {
                var texts = asset["texts"]!.AsObject();
                if (texts[locale] is null)
                {
                    texts[locale] = new JsonObject();
                }

                texts[locale]![name] = value!.GetValue<string>() is { Length: > 0 } text ? text : null;
            }
            else
            {
                asset[name] = value?.DeepClone() is JsonValue text && text.TryGetValue<string>(out var given) && given.Length == 0 ? null : value?.DeepClone();
            }
        }

        asset["updatedAt"] = clock = clock.AddMinutes(1);
        var answer = View(id, locale);
        answer.Remove("urls");
        return answer;
    }

    private JsonObject DeleteAsset(Guid id)
    {
        Assets.Remove(Asset(id));
        return [];
    }

    private JsonObject Replace(Guid id, ApiContent content)
    {
        var text = System.Text.Encoding.Latin1.GetString(content.Bytes.Span);
        var encoded = System.Text.RegularExpressions.Regex.Match(text, @"filename\*=UTF-8''([^\r\n;]+)").Groups[1].Value;
        var asset = Asset(id);
        var fileName = Uri.UnescapeDataString(encoded);
        asset["fileName"] = fileName;
        asset["urls"] = new JsonObject { ["original"] = $"/media/{id:D}/original/{fileName}", ["thumbnail"] = $"/media/{id:D}/thumbnail/{fileName}?v=2" };
        asset["updatedAt"] = clock = clock.AddMinutes(1);
        var answer = View(id, null);
        answer.Remove("urls");
        return answer;
    }

    private JsonObject SetPreset(string name, JsonObject options)
    {
        Presets.RemoveAll(preset => preset["name"]!.GetValue<string>() == name);
        var preset = new JsonObject { ["name"] = name, ["options"] = options.DeepClone() };
        Presets.Add(preset);
        return (JsonObject)preset.DeepClone();
    }

    private JsonObject DeletePreset(string name)
    {
        Presets.RemoveAll(preset => preset["name"]!.GetValue<string>() == name);
        return [];
    }

    private JsonObject ListEntries(string type, Dictionary<string, string> query)
    {
        var ids = query["filter[id][in]"].Split(',').Select(Guid.Parse).ToHashSet();
        var found = Entries.GetValueOrDefault(type, []).Where(entry => ids.Contains((Guid)entry["id"]!));
        return new JsonObject { ["items"] = new JsonArray([.. found.Select(entry => entry.DeepClone())]) };
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

