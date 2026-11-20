using System.Text.Json;
using System.Text.Json.Serialization;
using FluentCMS.Admin.Api;

namespace FluentCMS.Admin.Administration.Common;

// A role as the lists and the selects of the areas name it. The grants are the roles page's own to read.
public sealed record RoleSummary
{
    public required Guid Id { get; init; }

    // Null for a role of every site.
    public Guid? SiteId { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public bool BuiltIn { get; init; }
}

// GET /roles: the roles the caller manages or hands out, those of the site and those of every site, which the users, keys
// and members pages offer. A caller who may read none is answered 403.
internal static class RoleReads
{
    public static async Task<IReadOnlyList<RoleSummary>> GetRolesAsync(this IManagementApi api, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(api);
        return (await api.SendAsync(new ApiRequest(HttpMethod.Get, "roles"), CommonJsonContext.Default.ItemListRoleSummary, cancellationToken)).Value.Items;
    }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ItemList<RoleSummary>))]
internal sealed partial class CommonJsonContext : JsonSerializerContext;
