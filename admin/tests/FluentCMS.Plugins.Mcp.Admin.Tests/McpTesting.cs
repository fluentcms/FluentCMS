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
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace FluentCMS.Plugins.Mcp.Admin.Tests;

// The half's pages with the admin's services they inject: the MCP routes, the roles and the activity log in memory behind
// IManagementApi, the schema of the site, the shell's access, and an announcer that keeps what it was given.
public abstract class McpTestContext : BunitContext
{
    internal static readonly Guid Editor = new("0193a1b2-0000-7000-8000-00000000b001");
    internal static readonly Guid Reader = new("0193a1b2-0000-7000-8000-00000000b002");
    internal static readonly Guid SiteRole = new("0193a1b2-0000-7000-8000-00000000b003");

    protected McpTestContext()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddCmsComponents();
        Services.AddLocalization();
        Services.AddSingleton<IManagementApi>(Server);
        Services.AddSingleton<IAdminContext>(Admin);
        Services.AddSingleton<IAdminSchema>(Schema);
        Services.AddSingleton<IAnnouncer>(Announcer);
    }

    internal FakeMcp Server { get; } = new();

    internal FakeAdminContext Admin { get; } = new();

    internal FakeSchema Schema { get; } = new();

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
        var found = cut.FindAll("label").Last(element => element.TextContent.Trim().TrimEnd('*').TrimEnd() == label);
        return cut.Find("#" + found.GetAttribute("for"));
    }

    internal static ApiProblemException Problem(int status, string code, string? path = null) =>
        new(new ApiProblem { Status = status, Code = code, Errors = path is null ? [] : [new ApiProblemError { Path = path, Code = code, Message = "Refused." }] });
}

internal sealed class FakeAnnouncer : IAnnouncer
{
    public List<string> Messages { get; } = [];

    public void Announce(string message, AnnouncementPoliteness politeness = AnnouncementPoliteness.Polite) => Messages.Add(message);
}

// What the caller holds: every permission, unless the test takes one away, in the site or for the instance.
internal sealed class FakeAdminContext : IAdminContext, IAdminAccess
{
    public MeResponse? User => null;

    public MySiteInfo? Site => null;

    public string? ContentLocale => "en";

    public IAdminAccess Access => this;

    public HashSet<string> Denied { get; } = new(StringComparer.Ordinal);

    public HashSet<string> SiteOnly { get; } = new(StringComparer.Ordinal);

    public event Action? Changed;

    public bool Allows(string permission) => !Denied.Contains(permission);

    public bool AllowsForInstance(string permission) => Allows(permission) && !SiteOnly.Contains(permission);

    public TypeAccessInfo? ForType(string type) => null;

    public Task ReadSitesAsync(Guid? gone = null)
    {
        Changed?.Invoke();
        return Task.CompletedTask;
    }
}

internal sealed class FakeSchema : IAdminSchema
{
    public Task<SchemaDescription> GetSchemaAsync(CancellationToken cancellationToken = default) => Task.FromResult(new SchemaDescription
    {
        Types = [Type("article", "Article"), Type("product", null)],
        Components = [],
        Blocks = [],
    });

    private static SchemaTypeDescription Type(string name, string? displayName) => new() { Name = name, Kind = "collection", DisplayName = displayName, Fields = [] };
}

// The routes of the half as the server answers them, over clients, roles, instructions, prompts and activity rows in memory.
internal sealed class FakeMcp : IManagementApi
{
    public List<JsonObject> Clients { get; } = [];

    public List<JsonObject> Instructions { get; } = [];

    public List<JsonObject> Prompts { get; } = [];

    public List<JsonObject> Activity { get; } = [];

    public List<ApiRequest> Requests { get; } = [];

    // Answers a request with a problem instead, when it gives one.
    public Func<ApiRequest, ApiProblemException?>? Refuse { get; set; }

    public Guid AddClient(string name, string status, Guid? role = null)
    {
        var id = Guid.NewGuid();
        Clients.Add(new JsonObject
        {
            ["id"] = id,
            ["clientId"] = "mcp_" + id.ToString("N")[..8],
            ["name"] = name,
            ["status"] = status,
            ["roleId"] = role,
            ["roleName"] = role is { } known ? RoleName(known) : null,
            ["redirectUris"] = new JsonArray("http://127.0.0.1:33418/callback"),
            ["registeredAt"] = "2026-11-20T08:00:00+00:00",
            ["lastActivityAt"] = null,
        });
        return id;
    }

    public void AddInstruction(string? type, string text, bool orphaned = false) =>
        Instructions.Add(new JsonObject { ["type"] = type, ["text"] = text, ["orphaned"] = orphaned, ["updatedAt"] = DateTimeOffset.UtcNow.ToString("O"), ["updatedBy"] = Guid.Empty });

    public Guid AddPrompt(string name, string template, params string[] arguments)
    {
        var id = Guid.NewGuid();
        Prompts.Add(Prompt(id, new JsonObject
        {
            ["name"] = name,
            ["title"] = null,
            ["description"] = null,
            ["arguments"] = new JsonArray([.. arguments.Select(argument => (JsonNode)new JsonObject { ["name"] = argument, ["description"] = null, ["required"] = true })]),
            ["template"] = template,
        }));
        return id;
    }

    public void AddActivity(int count)
    {
        for (var index = 0; index < count; index++)
        {
            Activity.Add(new JsonObject
            {
                ["id"] = Guid.NewGuid(),
                ["occurredAt"] = "2026-11-21T09:00:00+00:00",
                ["actorName"] = "Ada",
                ["clientId"] = "mcp_desk",
                ["action"] = "content.created",
                ["targetType"] = "article",
                ["targetId"] = index.ToString(CultureInfo.InvariantCulture),
                ["targetLabel"] = $"Entry {index + 1}",
                ["locale"] = "en",
                ["origin"] = "mcp",
            });
        }
    }

    public JsonObject Client(Guid id) => Clients.First(client => (Guid)client["id"]! == id);

    public async Task<ApiResponse<T>> SendAsync<T>(ApiRequest request, JsonTypeInfo<T> answer, CancellationToken cancellationToken)
    {
        await Task.Yield();
        return new ApiResponse<T>(HttpStatusCode.OK, null, Answer(request).Deserialize(answer)!);
    }

    public async Task<ApiResponse> SendAsync(ApiRequest request, CancellationToken cancellationToken)
    {
        await Task.Yield();
        Answer(request);
        return new ApiResponse(HttpStatusCode.NoContent, null);
    }

    private static string RoleName(Guid role) => role == McpTestContext.Editor ? "Editor" : "Reader";

    private JsonNode Answer(ApiRequest request)
    {
        Requests.Add(request);
        if (Refuse?.Invoke(request) is { } refused)
        {
            throw refused;
        }

        var (path, query) = request.Path.Split('?') is [var before, var after] ? (before, after) : (request.Path, string.Empty);
        var segments = path.Split('/').Select(Uri.UnescapeDataString).ToArray();
        return (request.Method.Method, segments) switch
        {
            ("GET", ["roles"]) => Items(
            [
                new JsonObject { ["id"] = McpTestContext.Editor, ["siteId"] = null, ["name"] = "Editor" },
                new JsonObject { ["id"] = McpTestContext.Reader, ["siteId"] = null, ["name"] = "Reader" },
                new JsonObject { ["id"] = McpTestContext.SiteRole, ["siteId"] = Guid.NewGuid(), ["name"] = "Shop editor" },
            ]),
            ("GET", ["mcp", "clients"]) => Items(Clients),
            ("POST", ["mcp", "clients"]) => Register(request.Body!),
            ("POST", ["mcp", "clients", var id, "approve"]) => SetRole(Guid.Parse(id), request.Body!, "pending"),
            ("PUT", ["mcp", "clients", var id, "role"]) => SetRole(Guid.Parse(id), request.Body!, "approved"),
            ("POST", ["mcp", "clients", var id, "reject"]) => Reject(Guid.Parse(id)),
            ("DELETE", ["mcp", "clients", var id]) => Revoke(Guid.Parse(id)),
            ("GET", ["mcp", "instructions"]) => Items(Instructions),
            ("PUT", ["mcp", "instructions"]) => SaveInstruction(null, request.Body!),
            ("PUT", ["mcp", "instructions", var type]) => SaveInstruction(type, request.Body!),
            ("DELETE", ["mcp", "instructions"]) => DeleteInstruction(null),
            ("DELETE", ["mcp", "instructions", var type]) => DeleteInstruction(type),
            ("GET", ["mcp", "prompts"]) => Items(Prompts),
            ("POST", ["mcp", "prompts"]) => SavePrompt(null, request.Body!),
            ("PUT", ["mcp", "prompts", var id]) => SavePrompt(Guid.Parse(id), request.Body!),
            ("DELETE", ["mcp", "prompts", var id]) => DeletePrompt(Guid.Parse(id)),
            ("GET", ["activity"]) => ActivityPage(query),
            _ => throw new InvalidOperationException($"No answer for {request.Method} {request.Path}."),
        };
    }

    private static JsonObject Items(IEnumerable<JsonNode> items) => new() { ["items"] = new JsonArray([.. items.Select(item => item.DeepClone())]) };

    private JsonObject Found(Guid id) => Clients.FirstOrDefault(client => (Guid)client["id"]! == id) ?? throw McpTestContext.Problem(404, "FCMS1603");

    private JsonNode Register(JsonNode body)
    {
        var id = AddClient(body["name"]?.GetValue<string>() ?? "MCP client", "approved", Guid.Parse(body["role"]!.GetValue<string>()));
        Client(id)["redirectUris"] = body["redirectUris"]!.DeepClone();
        return Client(id).DeepClone();
    }

    private JsonNode SetRole(Guid id, JsonNode body, string from)
    {
        var client = Found(id);
        if ((string)client["status"]! != from)
        {
            throw McpTestContext.Problem(409, from == "pending" ? "FCMS1604" : "FCMS1607");
        }

        var role = Guid.Parse(body["role"]!.GetValue<string>());
        (client["status"], client["roleId"], client["roleName"]) = ("approved", role, RoleName(role));
        return client.DeepClone();
    }

    private JsonNode Reject(Guid id)
    {
        var client = Found(id);
        client["status"] = "rejected";
        return client.DeepClone();
    }

    private JsonObject Revoke(Guid id)
    {
        Clients.Remove(Found(id));
        return [];
    }

    private JsonNode SaveInstruction(string? type, JsonNode body)
    {
        Instructions.RemoveAll(row => (string?)row["type"] == type);
        AddInstruction(type, body["text"]!.GetValue<string>());
        return Instructions[^1].DeepClone();
    }

    private JsonObject DeleteInstruction(string? type)
    {
        if (Instructions.RemoveAll(row => (string?)row["type"] == type) == 0)
        {
            throw McpTestContext.Problem(404, "FCMS1622");
        }

        return [];
    }

    private static JsonObject Prompt(Guid id, JsonNode body)
    {
        var prompt = body.DeepClone().AsObject();
        prompt["id"] = id;
        prompt["siteId"] = Guid.Empty;
        prompt["updatedAt"] = "2026-11-21T09:00:00+00:00";
        prompt["updatedBy"] = Guid.Empty;
        return prompt;
    }

    private JsonNode SavePrompt(Guid? id, JsonNode body)
    {
        var name = body["name"]!.GetValue<string>();
        if (Prompts.Any(prompt => (string)prompt["name"]! == name && (Guid)prompt["id"]! != id))
        {
            throw McpTestContext.Problem(409, "FCMS1625");
        }

        if (id is { } existing)
        {
            var index = Prompts.FindIndex(prompt => (Guid)prompt["id"]! == existing);
            if (index < 0)
            {
                throw McpTestContext.Problem(404, "FCMS1624");
            }

            Prompts[index] = Prompt(existing, body);
            return Prompts[index].DeepClone();
        }

        Prompts.Add(Prompt(Guid.NewGuid(), body));
        return Prompts[^1].DeepClone();
    }

    private JsonObject DeletePrompt(Guid id)
    {
        if (Prompts.RemoveAll(prompt => (Guid)prompt["id"]! == id) == 0)
        {
            throw McpTestContext.Problem(404, "FCMS1624");
        }

        return [];
    }

    private JsonObject ActivityPage(string query)
    {
        var values = query.Split('&').Select(part => part.Split('=')).ToDictionary(part => part[0], part => part[1], StringComparer.Ordinal);
        var (page, size) = (int.Parse(values["page"], CultureInfo.InvariantCulture), int.Parse(values["pageSize"], CultureInfo.InvariantCulture));
        var rows = Activity.Where(row => (string)row["origin"]! == values["origin"]).ToList();
        return new JsonObject
        {
            ["items"] = new JsonArray([.. rows.Skip((page - 1) * size).Take(size).Select(row => row.DeepClone())]),
            ["total"] = rows.Count,
            ["page"] = page,
            ["pageSize"] = size,
        };
    }
}
