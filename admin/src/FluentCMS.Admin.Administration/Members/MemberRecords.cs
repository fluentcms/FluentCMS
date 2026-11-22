using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FluentCMS.Admin.Administration.Common;
using FluentCMS.Admin.Api;

namespace FluentCMS.Admin.Administration.Members;

// A member as GET /members and every change of a member answer it. A member belongs to no site.
internal sealed record MemberRecord
{
    public required Guid Id { get; init; }

    public required string Email { get; init; }

    public bool EmailVerified { get; init; }

    public string? DisplayName { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? LastLoginAt { get; init; }

    public DateTimeOffset? DeactivatedAt { get; init; }

    // Read only: no route ends a lockout, which ends by itself at this time.
    public DateTimeOffset? LockoutEnd { get; init; }

    public IReadOnlyList<MemberGroupRef> Groups { get; init; } = [];
}

internal sealed record MemberGroupRef
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }
}

internal sealed record MemberRecordPage
{
    public required IReadOnlyList<MemberRecord> Items { get; init; }

    public required long Total { get; init; }
}

// A sign-in of the member through a client, which revoking ends at its next request.
internal sealed record MemberSessionRecord
{
    public required Guid Id { get; init; }

    public string? ClientId { get; init; }

    public string? ClientName { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }
}

internal sealed record MemberLoginRecord
{
    public required string Provider { get; init; }

    public string? DisplayName { get; init; }
}

// A member group with the role its members hold, of the instance or of a site, and the number of its members.
internal sealed record MemberGroupRecord
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public required Guid RoleId { get; init; }

    public required string RoleName { get; init; }

    public Guid? RoleSiteId { get; init; }

    public int Members { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

// The calls of the members area (spec 4.15). Every one needs members.manage for the instance; making, changing or deleting a
// group, and putting a member in a group or taking one out, also need every grant of the group's role (FCMS3103).
internal static class MembersApi
{
    // The filters of the list, by the names of their query parameters, which the list's address and the API share.
    public const string GroupFilter = "group";
    public const string VerifiedFilter = "verified";
    public const string ActiveFilter = "active";

    public static async Task<RecordPage<MemberRecord>> GetMembersAsync(this IManagementApi api, RecordQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var path = $"members?page={query.Page.ToString(CultureInfo.InvariantCulture)}&pageSize={query.PageSize.ToString(CultureInfo.InvariantCulture)}"
            + (query.Search is { } search ? "&search=" + Uri.EscapeDataString(search) : "");
        foreach (var name in (string[])[GroupFilter, VerifiedFilter, ActiveFilter])
        {
            if (query.Filter(name) is { } value)
            {
                path += $"&{name}={Uri.EscapeDataString(value)}";
            }
        }

        var page = (await api.SendAsync(new ApiRequest(HttpMethod.Get, path), MembersJsonContext.Default.MemberRecordPage, cancellationToken)).Value;
        return new RecordPage<MemberRecord>(page.Items, page.Total);
    }

    public static Task<MemberRecord> GetMemberAsync(this IManagementApi api, Guid id, CancellationToken cancellationToken) =>
        api.MemberAsync(new ApiRequest(HttpMethod.Get, Member(id)), cancellationToken);

    // The member gets a link by mail to choose its password, which also verifies its address.
    public static async Task<MemberRecord> CreateMemberAsync(this IManagementApi api, string email, string? displayName, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(api);
        var body = new JsonObject { ["email"] = email, ["displayName"] = displayName };
        return (await api.SendAsync(new ApiRequest(HttpMethod.Post, "members") { Body = body }, MembersJsonContext.Default.MemberRecord, cancellationToken)).Value;
    }

    // An empty name clears it.
    public static Task<MemberRecord> RenameMemberAsync(this IManagementApi api, Guid id, string displayName, CancellationToken cancellationToken) =>
        api.MemberAsync(new ApiRequest(HttpMethod.Patch, Member(id)) { Body = new JsonObject { ["displayName"] = displayName } }, cancellationToken);

    public static Task DeleteMemberAsync(this IManagementApi api, Guid id, CancellationToken cancellationToken) =>
        api.SendAsync(new ApiRequest(HttpMethod.Delete, Member(id)), cancellationToken);

    public static Task<MemberRecord> DeactivateMemberAsync(this IManagementApi api, Guid id, CancellationToken cancellationToken) =>
        api.MemberAsync(new ApiRequest(HttpMethod.Post, Member(id) + "/deactivate"), cancellationToken);

    public static Task<MemberRecord> ReactivateMemberAsync(this IManagementApi api, Guid id, CancellationToken cancellationToken) =>
        api.MemberAsync(new ApiRequest(HttpMethod.Post, Member(id) + "/reactivate"), cancellationToken);

    public static async Task<IReadOnlyList<MemberSessionRecord>> GetMemberSessionsAsync(this IManagementApi api, Guid id, CancellationToken cancellationToken) =>
        (await api.SendAsync(new ApiRequest(HttpMethod.Get, Member(id) + "/sessions"), MembersJsonContext.Default.ItemListMemberSessionRecord, cancellationToken)).Value.Items;

    public static Task RevokeMemberSessionAsync(this IManagementApi api, Guid id, Guid session, CancellationToken cancellationToken) =>
        api.SendAsync(new ApiRequest(HttpMethod.Delete, $"{Member(id)}/sessions/{session:D}"), cancellationToken);

    public static async Task<IReadOnlyList<MemberLoginRecord>> GetMemberLoginsAsync(this IManagementApi api, Guid id, CancellationToken cancellationToken) =>
        (await api.SendAsync(new ApiRequest(HttpMethod.Get, Member(id) + "/logins"), MembersJsonContext.Default.ItemListMemberLoginRecord, cancellationToken)).Value.Items;

    public static Task RemoveMemberLoginAsync(this IManagementApi api, Guid id, string provider, CancellationToken cancellationToken) =>
        api.SendAsync(new ApiRequest(HttpMethod.Delete, $"{Member(id)}/logins/{Uri.EscapeDataString(provider)}"), cancellationToken);

    public static Task<MemberRecord> AddToGroupAsync(this IManagementApi api, Guid id, Guid group, CancellationToken cancellationToken) =>
        api.MemberAsync(new ApiRequest(HttpMethod.Put, $"{Member(id)}/groups/{group:D}"), cancellationToken);

    public static Task<MemberRecord> RemoveFromGroupAsync(this IManagementApi api, Guid id, Guid group, CancellationToken cancellationToken) =>
        api.MemberAsync(new ApiRequest(HttpMethod.Delete, $"{Member(id)}/groups/{group:D}"), cancellationToken);

    public static async Task<IReadOnlyList<MemberGroupRecord>> GetMemberGroupsAsync(this IManagementApi api, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(api);
        return (await api.SendAsync(new ApiRequest(HttpMethod.Get, "member-groups"), MembersJsonContext.Default.ItemListMemberGroupRecord, cancellationToken)).Value.Items;
    }

    public static Task<MemberGroupRecord> GetMemberGroupAsync(this IManagementApi api, Guid id, CancellationToken cancellationToken) =>
        api.GroupAsync(new ApiRequest(HttpMethod.Get, $"member-groups/{id:D}"), cancellationToken);

    public static Task<MemberGroupRecord> CreateMemberGroupAsync(this IManagementApi api, string name, string? description, Guid role, CancellationToken cancellationToken) =>
        api.GroupAsync(new ApiRequest(HttpMethod.Post, "member-groups") { Body = GroupBody(name, description, role) }, cancellationToken);

    // A PUT replaces the group: a description left empty clears it.
    public static Task<MemberGroupRecord> UpdateMemberGroupAsync(this IManagementApi api, Guid id, string name, string? description, Guid role, CancellationToken cancellationToken) =>
        api.GroupAsync(new ApiRequest(HttpMethod.Put, $"member-groups/{id:D}") { Body = GroupBody(name, description, role) }, cancellationToken);

    public static Task DeleteMemberGroupAsync(this IManagementApi api, Guid id, CancellationToken cancellationToken) =>
        api.SendAsync(new ApiRequest(HttpMethod.Delete, $"member-groups/{id:D}"), cancellationToken);

    private static string Member(Guid id) => $"members/{id:D}";

    private static JsonObject GroupBody(string name, string? description, Guid role) =>
        new() { ["name"] = name, ["description"] = description, ["roleId"] = role.ToString("D") };

    private static async Task<MemberRecord> MemberAsync(this IManagementApi api, ApiRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(api);
        return (await api.SendAsync(request, MembersJsonContext.Default.MemberRecord, cancellationToken)).Value;
    }

    private static async Task<MemberGroupRecord> GroupAsync(this IManagementApi api, ApiRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(api);
        return (await api.SendAsync(request, MembersJsonContext.Default.MemberGroupRecord, cancellationToken)).Value;
    }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(MemberRecord))]
[JsonSerializable(typeof(MemberRecordPage))]
[JsonSerializable(typeof(ItemList<MemberSessionRecord>))]
[JsonSerializable(typeof(ItemList<MemberLoginRecord>))]
[JsonSerializable(typeof(ItemList<MemberGroupRecord>))]
[JsonSerializable(typeof(MemberGroupRecord))]
internal sealed partial class MembersJsonContext : JsonSerializerContext;
