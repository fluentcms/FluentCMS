using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Channels;
using AngleSharp.Dom;
using Bunit;
using FluentCMS.Admin;
using FluentCMS.Admin.Actions;
using FluentCMS.Admin.Api;
using FluentCMS.Admin.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Shouldly;

namespace FluentCMS.Plugins.AiAssist.Admin.Tests;

// The half's actions and pages with the admin's services they use: the AI routes behind IManagementApi and IManagementEvents,
// which a test scripts, the shell's overlays, the site and an announcer that keeps what it was given.
public abstract class AiTestContext : BunitContext
{
    protected AiTestContext()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddCmsComponents();
        Services.AddLocalization();
        Services.AddSingleton<IManagementApi>(Server);
        Services.AddSingleton<IManagementEvents>(Server);
        Services.AddSingleton<IAdminContext>(Admin);
        Services.AddSingleton<IAnnouncer>(Announcer);
        Services.AddSingleton<IAdminOverlays>(Overlays);
    }

    internal FakeOverlays Overlays { get; } = new();

    internal FakeAiServer Server { get; } = new();

    internal FakeAdminContext Admin { get; } = new();

    internal FakeAnnouncer Announcer { get; } = new();

    protected static CancellationToken Token => Xunit.TestContext.Current.CancellationToken;

    protected IStringLocalizer<AiAssistResources> Texts => Services.GetRequiredService<IStringLocalizer<AiAssistResources>>();

    // The type of the tests: an article with a title, a summary, a hero image and a gallery.
    protected static SchemaTypeDescription Article { get; } = new()
    {
        Name = "article",
        Kind = "collection",
        Localized = true,
        Fields =
        [
            new SchemaFieldDescription { Name = "title", Type = "text", Validation = new() },
            new SchemaFieldDescription { Name = "summary", Type = "longText", Validation = new() },
            new SchemaFieldDescription { Name = "hero", Type = "media", Validation = new() },
            new SchemaFieldDescription { Name = "gallery", Type = "media", Multiple = true, Validation = new() },
        ],
    };

    // The form of an entry, a new one unless entryId names it, or of one not created yet.
    protected static FieldActionContext Context(string field, JsonObject document, string? locale = "en", Guid? entryId = null, bool created = true)
    {
        var values = document.ToDictionary(pair => pair.Key, pair => pair.Value);
        return new FieldActionContext
        {
            Type = Article,
            Field = Article.Fields.Single(item => item.Name == field),
            Value = values.GetValueOrDefault(field),
            Document = values,
            EntryId = created ? entryId ?? Guid.CreateVersion7() : null,
            ContentLocale = locale,
        };
    }

    // Runs the action and renders what it shows through the overlays.
    protected (Task<FieldActionResult> Result, IRenderedComponent<IComponent> Panel) Run(FieldActionBase action, FieldActionContext context)
    {
        Overlays.Shown.Clear();
        var result = action.RunAsync(context, Token);
        return (result, Render(Overlays.Host));
    }

    protected static IElement Button(IRenderedComponent<IComponent> panel, string text) =>
        panel.FindAll("fluent-button").Single(button => button.TextContent.Trim() == text);

    // The buttons with a text, which leaves out the dialog's own close button, named by its label.
    protected static string[] Buttons(IRenderedComponent<IComponent> panel) =>
        [.. panel.FindAll("fluent-button").Select(button => button.TextContent.Trim()).Where(text => text.Length > 0)];

    // The dialog's close, which the browser reports once the dialog has hidden and given focus back.
    protected static Task ClosedAsync(IRenderedComponent<IComponent> panel)
    {
        var dialog = panel.FindComponent<CmsDialog>();
        panel.WaitForAssertion(() => dialog.Instance.Open.ShouldBeFalse());
        return panel.InvokeAsync(() => dialog.Instance.OnClosed.InvokeAsync());
    }
}

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

internal sealed class FakeAnnouncer : IAnnouncer
{
    public List<string> Messages { get; } = [];

    public void Announce(string message, AnnouncementPoliteness politeness = AnnouncementPoliteness.Polite) => Messages.Add(message);
}

internal sealed class FakeAdminContext : IAdminContext, IAdminAccess
{
    public MeResponse? User => null;

    public MySiteInfo? Site { get; set; } = new()
    {
        Id = Guid.CreateVersion7(),
        Name = "Blog",
        Slug = "blog",
        DefaultLocale = "en",
        IsDefault = true,
        Locales = [new MySiteLocale { Code = "en", Position = 0, Enabled = true }, new MySiteLocale { Code = "fr", Position = 1, Enabled = true }, new MySiteLocale { Code = "de", Position = 2, Enabled = false }],
    };

    public string? ContentLocale => "en";

    public IAdminAccess Access => this;

    public event Action? Changed;

    public bool Allows(string permission) => true;

    public bool AllowsForInstance(string permission) => false;

    public bool AllowsForSomeResources(string permission) => Allows(permission);

    public TypeAccessInfo? ForType(string type) => null;

    public Task ReadSitesAsync(Guid? gone = null) => Task.CompletedTask;

    public void RaiseChanged() => Changed?.Invoke();
}

internal sealed record AiRequest(HttpMethod Method, string Path, JsonNode? Body, string? Locale, string? IfMatch);

// The AI and media routes as the server answers them. An action's events are what a test writes to Events, one at a time,
// or a problem the test sets before the first; GET /ai/usage answers Usage; media assets are read and their alt text saved;
// GET /ai/fields answers MayDescribeAssets as assets; GET /me/access answers by ReadOnlyIn; and an entry's saved draft is read
// in a locale from Drafts, a 404 where it holds none.
internal sealed class FakeAiServer : IManagementApi, IManagementEvents
{
    public List<AiRequest> Requests { get; } = [];

    public Channel<ApiEvent> Events { get; private set; } = Channel.CreateUnbounded<ApiEvent>();

    // Thrown in place of the first event, as the client throws a refusal before the provider is asked.
    public ApiProblem? Refusal { get; set; }

    // Whether the request of the stream that is read was cancelled.
    public bool StreamCancelled { get; private set; }

    public Dictionary<Guid, JsonObject> Assets { get; } = [];

    public Dictionary<(Guid Id, string? Locale), JsonObject> Drafts { get; } = [];

    // What GET /ai/fields answers as assets: whether the caller may send images of the library for alt text.
    public bool MayDescribeAssets { get; set; } = true;

    // The locales where GET /me/access says the caller may change no field of the article; in the others it may change
    // the title and the summary.
    public HashSet<string> ReadOnlyIn { get; } = [];

    public JsonObject Usage { get; set; } = UsageOf([]);

    public ApiProblem? UsageRefusal { get; set; }

    // The locale a read of an asset answers, from the locale it asked for: by default that one, else en.
    public Func<string?, string?> AnsweredLocale { get; set; } = asked => asked ?? "en";

    public void Delta(string text) => Events.Writer.TryWrite(new ApiEvent("delta", new JsonObject { ["text"] = text }.ToJsonString()));

    public void Done(JsonNode suggestion) =>
        Events.Writer.TryWrite(new ApiEvent("done", new JsonObject { ["suggestion"] = suggestion, ["usage"] = new JsonObject { ["provider"] = "openAi", ["model"] = "m" } }.ToJsonString()));

    public void Error(string code, string title, string detail) =>
        Events.Writer.TryWrite(new ApiEvent("error", new JsonObject { ["type"] = $"urn:fluentcms:error:{code}", ["title"] = title, ["status"] = 502, ["detail"] = detail, ["code"] = code }.ToJsonString()));

    public Guid AddImage(string fileName, string? alt = null, string mimeType = "image/png")
    {
        var id = Guid.CreateVersion7();
        Assets[id] = new JsonObject { ["id"] = id, ["fileName"] = fileName, ["mimeType"] = mimeType, ["alt"] = alt, ["locale"] = "en", ["version"] = 3 };
        return id;
    }

    public async IAsyncEnumerable<ApiEvent> ReadAsync(ApiRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Record(request);
        if (Refusal is { } refusal)
        {
            throw new ApiProblemException(refusal);
        }

        using var registration = cancellationToken.Register(() => StreamCancelled = true);
        await foreach (var sent in Events.Reader.ReadAllAsync(cancellationToken))
        {
            yield return sent;
        }
    }

    public Task<ApiResponse<T>> SendAsync<T>(ApiRequest request, JsonTypeInfo<T> answer, CancellationToken cancellationToken)
    {
        Record(request);
        var path = request.Path.Split('?')[0];
        if (path == "ai/usage")
        {
            return UsageRefusal is { } refused
                ? Task.FromException<ApiResponse<T>>(new ApiProblemException(refused))
                : Task.FromResult(new ApiResponse<T>(HttpStatusCode.OK, null, Usage.Deserialize(answer)!));
        }

        if (path.StartsWith("media/", StringComparison.Ordinal) && Guid.TryParse(path["media/".Length..], out var id) && Assets.TryGetValue(id, out var asset))
        {
            var read = asset.DeepClone().AsObject();
            read["locale"] = AnsweredLocale(request.Locale);
            return Task.FromResult(new ApiResponse<T>(HttpStatusCode.OK, $"\"{asset["version"]}\"", read.Deserialize(answer)!));
        }

        if (path == "me/access")
        {
            var update = !ReadOnlyIn.Contains(request.Locale ?? "en");
            var access = new JsonObject { ["types"] = new JsonObject { ["article"] = new JsonObject { ["update"] = update, ["updatableFields"] = update ? new JsonArray("title", "summary") : new JsonArray() } } };
            return Task.FromResult(new ApiResponse<T>(HttpStatusCode.OK, null, access.Deserialize(answer)!));
        }

        if (path == "ai/fields")
        {
            var fields = new JsonObject { ["enabled"] = true, ["locale"] = "en", ["fields"] = new JsonObject(), ["assets"] = MayDescribeAssets };
            return Task.FromResult(new ApiResponse<T>(HttpStatusCode.OK, null, fields.Deserialize(answer)!));
        }

        if (path.StartsWith("content/article/", StringComparison.Ordinal) && Guid.TryParse(path["content/article/".Length..], out var entry) && Drafts.TryGetValue((entry, request.Locale), out var draft))
        {
            return Task.FromResult(new ApiResponse<T>(HttpStatusCode.OK, "\"1\"", draft.Deserialize(answer)!));
        }

        return Task.FromException<ApiResponse<T>>(new ApiProblemException(new ApiProblem { Status = 404, Code = "FCMS3104", Title = "Not found" }));
    }

    public Task<ApiResponse> SendAsync(ApiRequest request, CancellationToken cancellationToken)
    {
        Record(request);
        if (request.Method == HttpMethod.Patch && Guid.TryParse(request.Path["media/".Length..], out var id) && Assets.TryGetValue(id, out var asset))
        {
            if (request.IfMatch != $"\"{asset["version"]}\"")
            {
                return Task.FromException<ApiResponse>(new ApiProblemException(new ApiProblem { Status = 412, Code = "FCMS1560", Title = "The asset was changed" }));
            }

            asset["alt"] = request.Body!["alt"]!.DeepClone();
            asset["version"] = asset["version"]!.GetValue<int>() + 1;
            return Task.FromResult(new ApiResponse(HttpStatusCode.OK, $"\"{asset["version"]}\""));
        }

        return Task.FromException<ApiResponse>(new ApiProblemException(new ApiProblem { Status = 404, Code = "FCMS3104", Title = "Not found" }));
    }

    public static JsonObject UsageOf(IEnumerable<JsonObject> rows, int page = 1, long? total = null, bool groupsTruncated = false)
    {
        var all = rows.ToList();
        return new JsonObject
        {
            ["items"] = new JsonArray([.. all.Select(row => (JsonNode)row.DeepClone())]),
            ["total"] = total ?? all.Count,
            ["page"] = page,
            ["pageSize"] = 50,
            ["totals"] = new JsonObject
            {
                ["calls"] = total ?? all.Count,
                ["promptTokens"] = all.Sum(row => row["promptTokens"]!.GetValue<long>()),
                ["completionTokens"] = all.Sum(row => row["completionTokens"]!.GetValue<long>()),
                ["groups"] = new JsonArray([.. all.GroupBy(row => (row["principalId"]!.GetValue<Guid>(), row["action"]!.GetValue<string>())).Select(group => (JsonNode)new JsonObject
                {
                    ["principalKind"] = group.First()["principalKind"]!.DeepClone(),
                    ["principalId"] = group.Key.Item1,
                    ["action"] = group.Key.Item2,
                    ["calls"] = group.Count(),
                    ["promptTokens"] = group.Sum(row => row["promptTokens"]!.GetValue<long>()),
                    ["completionTokens"] = group.Sum(row => row["completionTokens"]!.GetValue<long>()),
                })]),
                ["groupsTruncated"] = groupsTruncated,
            },
        };
    }

    private void Record(ApiRequest request) => Requests.Add(new AiRequest(request.Method, request.Path, request.Body?.DeepClone(), request.Locale, request.IfMatch));
}
