using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FluentCMS.Admin.Api;

namespace FluentCMS.Plugins.Mcp.Admin.Api;

// The records of the MCP routes the half reads, its own rather than the server half's (spec 5.1), which the API tests hold to
// the reference host's OpenAPI document (spec 7). Each reads the members the half shows and skips the others.

// A client of the Management API, which belongs to the instance. Status is pending, approved or rejected.
internal sealed record McpClient
{
    public required Guid Id { get; init; }

    public required string ClientId { get; init; }

    public required string Name { get; init; }

    public required string Status { get; init; }

    public Guid? RoleId { get; init; }

    // Null while the client has no role, or once its role was deleted.
    public string? RoleName { get; init; }

    public required IReadOnlyList<string> RedirectUris { get; init; }

    public required DateTimeOffset RegisteredAt { get; init; }

    // The time of the newest token issued to the client.
    public DateTimeOffset? LastActivityAt { get; init; }
}

// A role as GET /roles lists it; null SiteId is a role of every site.
internal sealed record McpRole
{
    public required Guid Id { get; init; }

    public Guid? SiteId { get; init; }

    public required string Name { get; init; }
}

// The guidelines of the site when Type is null, else the instructions of one type. Orphaned: the type is no longer in the
// schema, so no agent is served the text.
internal sealed record McpInstruction
{
    public string? Type { get; init; }

    public required string Text { get; init; }

    public bool Orphaned { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

internal sealed record McpPrompt
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public string? Title { get; init; }

    public string? Description { get; init; }

    public required IReadOnlyList<McpPromptArgument> Arguments { get; init; }

    public required string Template { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

internal sealed record McpPromptArgument
{
    public string? Name { get; init; }

    public string? Description { get; init; }

    public bool? Required { get; init; }
}

// A page of GET /activity.
internal sealed record McpActivityAnswer
{
    public required IReadOnlyList<McpActivityRow> Items { get; init; }

    public required long Total { get; init; }

    public required int Page { get; init; }

    public required int PageSize { get; init; }
}

internal sealed record McpActivityRow
{
    public required Guid Id { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    public required string ActorName { get; init; }

    public string? ClientId { get; init; }

    public required string Action { get; init; }

    public string? TargetType { get; init; }

    public string? TargetId { get; init; }

    public string? TargetLabel { get; init; }

    public string? Locale { get; init; }
}

// The calls of the half (spec 5.4). They go to the site the shell shows, where the plugin is enabled, since the server
// answers the plugin's routes only there; none is about content, so none sends a content locale. The clients need
// mcp.manage for the instance, the instructions and prompts mcp.manage in the site, and the agent writes activity.read there.
internal static class McpApi
{
    public const int ActivityPageSize = 50;

    public static async Task<IReadOnlyList<McpClient>> GetClientsAsync(this IManagementApi api, CancellationToken cancellationToken) =>
        (await api.SendAsync(Request(HttpMethod.Get, "mcp/clients"), McpJsonContext.Default.ItemListMcpClient, cancellationToken)).Value.Items;

    // Approved at once with the role, whatever the registration policy says.
    public static async Task<McpClient> RegisterClientAsync(this IManagementApi api, string? name, IReadOnlyList<string> redirectUris, string role, CancellationToken cancellationToken)
    {
        var body = new JsonObject { ["name"] = name, ["redirectUris"] = new JsonArray([.. redirectUris.Select(uri => (JsonNode)uri)]), ["role"] = role };
        return (await api.SendAsync(Request(HttpMethod.Post, "mcp/clients", body), McpJsonContext.Default.McpClient, cancellationToken)).Value;
    }

    public static async Task<McpClient> ApproveClientAsync(this IManagementApi api, Guid id, string role, CancellationToken cancellationToken) =>
        (await api.SendAsync(Request(HttpMethod.Post, Client(id) + "/approve", new JsonObject { ["role"] = role }), McpJsonContext.Default.McpClient, cancellationToken)).Value;

    // Ends the client's tokens and its users' consents.
    public static async Task<McpClient> ChangeClientRoleAsync(this IManagementApi api, Guid id, string role, CancellationToken cancellationToken) =>
        (await api.SendAsync(Request(HttpMethod.Put, Client(id) + "/role", new JsonObject { ["role"] = role }), McpJsonContext.Default.McpClient, cancellationToken)).Value;

    public static async Task<McpClient> RejectClientAsync(this IManagementApi api, Guid id, CancellationToken cancellationToken) =>
        (await api.SendAsync(Request(HttpMethod.Post, Client(id) + "/reject"), McpJsonContext.Default.McpClient, cancellationToken)).Value;

    public static Task RevokeClientAsync(this IManagementApi api, Guid id, CancellationToken cancellationToken) =>
        api.SendAsync(Request(HttpMethod.Delete, Client(id)), cancellationToken);

    // A caller who may read no role is answered 403.
    public static async Task<IReadOnlyList<McpRole>> GetRolesAsync(this IManagementApi api, CancellationToken cancellationToken) =>
        (await api.SendAsync(Request(HttpMethod.Get, "roles"), McpJsonContext.Default.ItemListMcpRole, cancellationToken)).Value.Items;

    public static async Task<IReadOnlyList<McpInstruction>> GetInstructionsAsync(this IManagementApi api, CancellationToken cancellationToken) =>
        (await api.SendAsync(Request(HttpMethod.Get, "mcp/instructions"), McpJsonContext.Default.ItemListMcpInstruction, cancellationToken)).Value.Items;

    // The guidelines of the site when the type is null.
    public static async Task<McpInstruction> SaveInstructionAsync(this IManagementApi api, string? type, string text, CancellationToken cancellationToken) =>
        (await api.SendAsync(Request(HttpMethod.Put, Instruction(type), new JsonObject { ["text"] = text }), McpJsonContext.Default.McpInstruction, cancellationToken)).Value;

    public static Task DeleteInstructionAsync(this IManagementApi api, string? type, CancellationToken cancellationToken) =>
        api.SendAsync(Request(HttpMethod.Delete, Instruction(type)), cancellationToken);

    public static async Task<IReadOnlyList<McpPrompt>> GetPromptsAsync(this IManagementApi api, CancellationToken cancellationToken) =>
        (await api.SendAsync(Request(HttpMethod.Get, "mcp/prompts"), McpJsonContext.Default.ItemListMcpPrompt, cancellationToken)).Value.Items;

    // The whole prompt: a create when the id is null, else its replacement.
    public static async Task<McpPrompt> SavePromptAsync(this IManagementApi api, Guid? id, McpPromptDraft draft, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["name"] = draft.Name,
            ["title"] = draft.Title,
            ["description"] = draft.Description,
            ["arguments"] = new JsonArray([.. draft.Arguments.Select(argument => (JsonNode)new JsonObject
            {
                ["name"] = argument.Name,
                ["description"] = argument.Description,
                ["required"] = argument.Required,
            })]),
            ["template"] = draft.Template,
        };
        var request = id is { } existing ? Request(HttpMethod.Put, Prompt(existing), body) : Request(HttpMethod.Post, "mcp/prompts", body);
        return (await api.SendAsync(request, McpJsonContext.Default.McpPrompt, cancellationToken)).Value;
    }

    public static Task DeletePromptAsync(this IManagementApi api, Guid id, CancellationToken cancellationToken) =>
        api.SendAsync(Request(HttpMethod.Delete, Prompt(id)), cancellationToken);

    // What agents did in the site, newest first.
    public static async Task<McpActivityAnswer> GetAgentWritesAsync(this IManagementApi api, int page, CancellationToken cancellationToken)
    {
        var path = string.Create(CultureInfo.InvariantCulture, $"activity?origin=mcp&page={page}&pageSize={ActivityPageSize}");
        return (await api.SendAsync(Request(HttpMethod.Get, path), McpJsonContext.Default.McpActivityAnswer, cancellationToken)).Value;
    }

    private static string Client(Guid id) => $"mcp/clients/{id:D}";

    private static string Instruction(string? type) => type is null ? "mcp/instructions" : $"mcp/instructions/{Uri.EscapeDataString(type)}";

    private static string Prompt(Guid id) => $"mcp/prompts/{id:D}";

    private static ApiRequest Request(HttpMethod method, string path, JsonNode? body = null) => new(method, path) { Body = body, NoLocale = true };
}

// A prompt as the form holds it.
internal sealed class McpPromptDraft
{
    public string? Name { get; set; }

    public string? Title { get; set; }

    public string? Description { get; set; }

    public List<McpArgumentDraft> Arguments { get; } = [];

    public string? Template { get; set; }

    public static McpPromptDraft From(McpPrompt prompt)
    {
        var draft = new McpPromptDraft { Name = prompt.Name, Title = prompt.Title, Description = prompt.Description, Template = prompt.Template };
        draft.Arguments.AddRange(prompt.Arguments.Select(argument => new McpArgumentDraft { Name = argument.Name, Description = argument.Description, Required = argument.Required == true }));
        return draft;
    }
}

internal sealed class McpArgumentDraft
{
    public string? Name { get; set; }

    public string? Description { get; set; }

    public bool Required { get; set; }
}

// The answers of the routes the pages tell apart, by code and path only.
internal static class McpProblems
{
    public const string ClientNotFound = "FCMS1603";
    public const string ClientNotPending = "FCMS1604";
    public const string ClientRoleRequired = "FCMS1605";
    public const string ClientRoleUnknown = "FCMS1606";
    public const string ClientNotApproved = "FCMS1607";
    public const string ClientRegistrationInvalid = "FCMS1608";
    public const string ClientChangedMeanwhile = "FCMS1609";
    public const string InvalidInstruction = "FCMS1620";
    public const string InstructionTypeNotFound = "FCMS1621";
    public const string InstructionNotFound = "FCMS1622";
    public const string InvalidPrompt = "FCMS1623";
    public const string PromptNotFound = "FCMS1624";
    public const string PromptNameTaken = "FCMS1625";
    public const string PromptNameReserved = "FCMS1626";
    public const string UndeclaredPromptArgument = "FCMS1627";

    // What a call may throw: a problem, or a server that did not answer or answered nonsense.
    public static bool IsFailure(Exception exception) =>
        exception is ApiProblemException or HttpRequestException or TaskCanceledException or JsonException;

    public static bool Has(Exception exception, string code) =>
        exception is ApiProblemException { Problem: var problem }
        && (problem.Code == code || problem.Errors.Any(error => error.Code == code));

    // The paths of the members a validation refused, each with its code.
    public static IEnumerable<(string Path, string Code)> Errors(Exception exception) =>
        exception is ApiProblemException { Problem: var problem } ? problem.Errors.Select(error => (error.Path ?? string.Empty, error.Code ?? string.Empty)) : [];

    public static bool IsForbidden(Exception exception) => exception is ApiProblemException { Problem.Status: 403 };
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(McpClient))]
[JsonSerializable(typeof(ItemList<McpClient>))]
[JsonSerializable(typeof(ItemList<McpRole>))]
[JsonSerializable(typeof(McpInstruction))]
[JsonSerializable(typeof(ItemList<McpInstruction>))]
[JsonSerializable(typeof(McpPrompt))]
[JsonSerializable(typeof(ItemList<McpPrompt>))]
[JsonSerializable(typeof(McpActivityAnswer))]
internal sealed partial class McpJsonContext : JsonSerializerContext;
