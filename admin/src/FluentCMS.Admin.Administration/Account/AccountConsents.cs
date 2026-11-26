using System.Text.Json;
using System.Text.Json.Serialization;
using FluentCMS.Admin.Api;

namespace FluentCMS.Admin.Administration.Account;

// An MCP client that counts toward the user's maxConsentsPerUser, from GET /mcp/consents of the MCP plugin (#3441). Active
// tells whether the user's consent to it is valid; a client still counts while it is not, until the consent is ended.
internal sealed record MyMcpConsent
{
    public required Guid Id { get; init; }

    public required string ClientId { get; init; }

    public required string Name { get; init; }

    public DateTimeOffset? ConsentedAt { get; init; }

    public required bool Active { get; init; }
}

// The caller's own consents of the MCP plugin. They belong to the instance, but the plugin's routes answer only in a site
// where it is enabled, so they go to the site the shell shows, as the MCP half's calls do; none sends a content locale.
internal static class AccountConsentsApi
{
    public const string Plugin = "mcp";

    public static async Task<IReadOnlyList<MyMcpConsent>> GetMyMcpConsentsAsync(this IManagementApi api, string site, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(api);
        return (await api.SendAsync(Request(HttpMethod.Get, "mcp/consents", site), AccountJsonContext.Default.ItemListMyMcpConsent, cancellationToken)).Value.Items;
    }

    // Ends the consent with every token issued under it and frees the client's place.
    public static Task EndMyMcpConsentAsync(this IManagementApi api, string site, Guid id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(api);
        return api.SendAsync(Request(HttpMethod.Delete, $"mcp/consents/{id:D}", site), cancellationToken);
    }

    private static ApiRequest Request(HttpMethod method, string path, string site) => new(method, path) { Site = site, NoLocale = true };
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ItemList<MyMcpConsent>))]
internal sealed partial class AccountJsonContext : JsonSerializerContext;
