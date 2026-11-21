using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FluentCMS.Admin.Administration.Common;
using FluentCMS.Admin.Api;

namespace FluentCMS.Admin.Administration.Users;

// A user as GET /users and every change of a user answer it. The answer does not say whether the user has a password, so
// the forced change is offered to every user and a user without one is refused it (FCMS3049 on mustChangePassword).
internal sealed record UserRecord
{
    public required Guid Id { get; init; }

    public required string UserName { get; init; }

    public string? Email { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? DeactivatedAt { get; init; }

    public bool MustChangePassword { get; init; }

    public bool TwoFactorEnabled { get; init; }

    // Read only: no route ends a lockout, which ends by itself at this time.
    public DateTimeOffset? LockoutEnd { get; init; }

    public IReadOnlyList<UserRoleRecord> Roles { get; init; } = [];
}

internal sealed record UserRoleRecord
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    // Null for a role of every site.
    public Guid? SiteId { get; init; }
}

internal sealed record UserRecordPage
{
    public required IReadOnlyList<UserRecord> Items { get; init; }

    public required long Total { get; init; }
}

// A sign-in of the user through a client, which revoking ends at its next request.
internal sealed record UserSessionRecord
{
    public required Guid Id { get; init; }

    public string? ClientId { get; init; }

    public string? ClientName { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }
}

internal sealed record UserLoginRecord
{
    public required string Provider { get; init; }

    public string? DisplayName { get; init; }
}

// A pending invitation. The list leaves out one whose sender no longer stands, which accept would refuse (#3391).
internal sealed record InvitationRecord
{
    public required Guid Id { get; init; }

    public required string Email { get; init; }

    public IReadOnlyList<Guid> RoleIds { get; init; } = [];

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }
}

internal sealed record InvitationSentRecord
{
    public required Guid Id { get; init; }

    public required string Email { get; init; }

    public DateTimeOffset ExpiresAt { get; init; }
}

// The calls of the users area (spec 4.15). Every one needs users.manage for the instance; a change of a user also needs
// every grant of its roles, and a role is given or taken, and an invitation sent or revoked, only by one who holds every
// grant of its roles (FCMS3103).
internal static class UsersApi
{
    public static async Task<RecordPage<UserRecord>> GetUsersAsync(this IManagementApi api, RecordQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var path = $"users?page={query.Page.ToString(CultureInfo.InvariantCulture)}&pageSize={query.PageSize.ToString(CultureInfo.InvariantCulture)}"
            + (query.Search is { } search ? "&search=" + Uri.EscapeDataString(search) : "");
        var page = (await api.SendAsync(new ApiRequest(HttpMethod.Get, path), UsersJsonContext.Default.UserRecordPage, cancellationToken)).Value;
        return new RecordPage<UserRecord>(page.Items, page.Total);
    }

    public static Task<UserRecord> GetUserAsync(this IManagementApi api, Guid id, CancellationToken cancellationToken) =>
        api.UserAsync(new ApiRequest(HttpMethod.Get, User(id)), cancellationToken);

    public static Task<UserRecord> SetMustChangePasswordAsync(this IManagementApi api, Guid id, bool mustChange, CancellationToken cancellationToken) =>
        api.UserAsync(new ApiRequest(HttpMethod.Patch, User(id)) { Body = new JsonObject { ["mustChangePassword"] = mustChange } }, cancellationToken);

    public static Task<UserRecord> DeactivateAsync(this IManagementApi api, Guid id, CancellationToken cancellationToken) =>
        api.UserAsync(new ApiRequest(HttpMethod.Post, User(id) + "/deactivate"), cancellationToken);

    public static Task<UserRecord> ReactivateAsync(this IManagementApi api, Guid id, CancellationToken cancellationToken) =>
        api.UserAsync(new ApiRequest(HttpMethod.Post, User(id) + "/reactivate"), cancellationToken);

    public static Task<UserRecord> AddRoleAsync(this IManagementApi api, Guid id, Guid role, CancellationToken cancellationToken) =>
        api.UserAsync(new ApiRequest(HttpMethod.Put, $"{User(id)}/roles/{role:D}"), cancellationToken);

    public static Task<UserRecord> RemoveRoleAsync(this IManagementApi api, Guid id, Guid role, CancellationToken cancellationToken) =>
        api.UserAsync(new ApiRequest(HttpMethod.Delete, $"{User(id)}/roles/{role:D}"), cancellationToken);

    public static Task<UserRecord> ResetTwoFactorAsync(this IManagementApi api, Guid id, CancellationToken cancellationToken) =>
        api.UserAsync(new ApiRequest(HttpMethod.Post, User(id) + "/two-factor/reset"), cancellationToken);

    public static async Task<IReadOnlyList<UserSessionRecord>> GetSessionsAsync(this IManagementApi api, Guid id, CancellationToken cancellationToken) =>
        (await api.SendAsync(new ApiRequest(HttpMethod.Get, User(id) + "/sessions"), UsersJsonContext.Default.ItemListUserSessionRecord, cancellationToken)).Value.Items;

    public static Task RevokeSessionAsync(this IManagementApi api, Guid id, Guid session, CancellationToken cancellationToken) =>
        api.SendAsync(new ApiRequest(HttpMethod.Delete, $"{User(id)}/sessions/{session:D}"), cancellationToken);

    public static async Task<IReadOnlyList<UserLoginRecord>> GetLoginsAsync(this IManagementApi api, Guid id, CancellationToken cancellationToken) =>
        (await api.SendAsync(new ApiRequest(HttpMethod.Get, User(id) + "/logins"), UsersJsonContext.Default.ItemListUserLoginRecord, cancellationToken)).Value.Items;

    public static Task RemoveLoginAsync(this IManagementApi api, Guid id, string provider, CancellationToken cancellationToken) =>
        api.SendAsync(new ApiRequest(HttpMethod.Delete, $"{User(id)}/logins/{Uri.EscapeDataString(provider)}"), cancellationToken);

    public static async Task<IReadOnlyList<InvitationRecord>> GetInvitationsAsync(this IManagementApi api, CancellationToken cancellationToken) =>
        (await api.SendAsync(new ApiRequest(HttpMethod.Get, "invitations"), UsersJsonContext.Default.ItemListInvitationRecord, cancellationToken)).Value.Items;

    public static async Task<InvitationSentRecord> InviteAsync(this IManagementApi api, string email, IEnumerable<Guid> roles, CancellationToken cancellationToken)
    {
        var body = new JsonObject { ["email"] = email, ["roleIds"] = new JsonArray([.. roles.Select(role => (JsonNode)role.ToString("D"))]) };
        return (await api.SendAsync(new ApiRequest(HttpMethod.Post, "invitations") { Body = body }, UsersJsonContext.Default.InvitationSentRecord, cancellationToken)).Value;
    }

    public static Task RevokeInvitationAsync(this IManagementApi api, Guid id, CancellationToken cancellationToken) =>
        api.SendAsync(new ApiRequest(HttpMethod.Delete, $"invitations/{id:D}"), cancellationToken);

    private static string User(Guid id) => $"users/{id:D}";

    private static async Task<UserRecord> UserAsync(this IManagementApi api, ApiRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(api);
        return (await api.SendAsync(request, UsersJsonContext.Default.UserRecord, cancellationToken)).Value;
    }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(UserRecord))]
[JsonSerializable(typeof(UserRecordPage))]
[JsonSerializable(typeof(ItemList<UserSessionRecord>))]
[JsonSerializable(typeof(ItemList<UserLoginRecord>))]
[JsonSerializable(typeof(ItemList<InvitationRecord>))]
[JsonSerializable(typeof(InvitationSentRecord))]
internal sealed partial class UsersJsonContext : JsonSerializerContext;
