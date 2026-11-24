using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentCMS.Agents;

namespace FluentCMS.Core.Identity.Oidc;

// The state of an MCP client that registered itself, kept in one property of its OpenIddict application, so that it needs
// no table of its own. A property that cannot be read, or that says approved without a role, is a client that waits: what
// is damaged never counts as approved.
internal sealed record McpClientState(McpClientStatus Status, DateTimeOffset RegisteredAt)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    // The role that limits the client's tokens, set when it is approved.
    public Guid? RoleId { get; init; }

    // Null when the open policy approved it.
    public Guid? ApprovedBy { get; init; }

    public DateTimeOffset? ApprovedAt { get; init; }

    // When a user first consented to the client; null while it was never used.
    public DateTimeOffset? UsedAt { get; init; }

    // The user whose consent that was. A consent that is revoked and then pruned leaves no row, and the place it took
    // among the clients a user may keep stays that user's for as long as the client lives (ClientConsents).
    public Guid? UsedBy { get; init; }

    [JsonIgnore]
    public bool IsApproved => Status == McpClientStatus.Approved && RoleId is not null;

    // Null for a client that did not register itself.
    public static McpClientState? Of(ImmutableDictionary<string, JsonElement> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);

        if (!properties.TryGetValue(AuthorizationServer.McpClientProperty, out var element))
        {
            return null;
        }

        try
        {
            if (element.ValueKind == JsonValueKind.Object && element.Deserialize<McpClientState>(Json) is { } state)
            {
                return state.IsApproved || state.Status != McpClientStatus.Approved ? state : state with { Status = McpClientStatus.Pending };
            }
        }
        catch (JsonException)
        {
        }

        return new McpClientState(McpClientStatus.Pending, DateTimeOffset.MinValue);
    }

    // The same, from the stored JSON of an application's properties, as a query reads it.
    public static McpClientState? OfStored(string? properties)
    {
        if (string.IsNullOrEmpty(properties))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(properties);
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty(AuthorizationServer.McpClientProperty, out var element)
                ? Of(ImmutableDictionary<string, JsonElement>.Empty.Add(AuthorizationServer.McpClientProperty, element.Clone()))
                : null;
        }
        catch (JsonException)
        {
            return new McpClientState(McpClientStatus.Pending, DateTimeOffset.MinValue);
        }
    }

    public JsonElement ToProperty() => JsonSerializer.SerializeToElement(this, Json);

    // What the stored state of a client first used by the user holds, for a query over the stored properties.
    public static string UsedByMarker(Guid user) => $"\"usedBy\":\"{user:D}\"";
}
