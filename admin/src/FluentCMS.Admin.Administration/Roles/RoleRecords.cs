using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FluentCMS.Admin.Api;

namespace FluentCMS.Admin.Administration.Roles;

// A role as GET, POST and PUT /roles answer it. A role has no ETag: a save replaces it, and one that met another's change
// is refused with FCMS3045.
internal sealed record RoleRecord
{
    public required Guid Id { get; init; }

    // Null for a role of every site. A save keeps the site the role was made in.
    public Guid? SiteId { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public bool BuiltIn { get; init; }

    public bool RequireTwoFactor { get; init; }

    public IReadOnlyList<RoleGrantRecord> Grants { get; init; } = [];
}

// A grant names a permission of the catalogue or a pattern of them. Its constraints are kept as the server wrote them, every
// member, so that a save, which replaces every grant, sends back each one as it was read.
internal sealed record RoleGrantRecord
{
    public required string Permission { get; init; }

    public JsonObject? Constraints { get; init; }
}

// A permission of GET /permissions: core's, one per content type and operation, a plugin's or a settings area's.
public sealed record PermissionRecord
{
    public required string Name { get; init; }

    public required string Description { get; init; }

    // core, content, plugin or settings.
    public required string Source { get; init; }
}

// The calls of the roles area (spec 4.15). Each write needs roles.manage where the role applies, and every grant the role had
// and gets (FCMS3103).
internal static class RolesApi
{
    public static async Task<IReadOnlyList<PermissionRecord>> GetPermissionsAsync(this IManagementApi api, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(api);
        return (await api.SendAsync(new ApiRequest(HttpMethod.Get, "permissions"), RolesJsonContext.Default.ItemListPermissionRecord, cancellationToken)).Value.Items;
    }

    public static Task<RoleRecord> GetRoleAsync(this IManagementApi api, Guid id, CancellationToken cancellationToken) =>
        api.RoleAsync(new ApiRequest(HttpMethod.Get, $"roles/{id:D}"), cancellationToken);

    public static Task<RoleRecord> CreateRoleAsync(this IManagementApi api, JsonObject body, CancellationToken cancellationToken) =>
        api.RoleAsync(new ApiRequest(HttpMethod.Post, "roles") { Body = body }, cancellationToken);

    public static Task<RoleRecord> UpdateRoleAsync(this IManagementApi api, Guid id, JsonObject body, CancellationToken cancellationToken) =>
        api.RoleAsync(new ApiRequest(HttpMethod.Put, $"roles/{id:D}") { Body = body }, cancellationToken);

    public static Task DeleteRoleAsync(this IManagementApi api, Guid id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(api);
        return api.SendAsync(new ApiRequest(HttpMethod.Delete, $"roles/{id:D}"), cancellationToken);
    }

    private static async Task<RoleRecord> RoleAsync(this IManagementApi api, ApiRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(api);
        return (await api.SendAsync(request, RolesJsonContext.Default.RoleRecord, cancellationToken)).Value;
    }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(RoleRecord))]
[JsonSerializable(typeof(ItemList<PermissionRecord>))]
internal sealed partial class RolesJsonContext : JsonSerializerContext;
