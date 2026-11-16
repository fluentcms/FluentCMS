namespace FluentCMS.Admin.Api;

/// <summary>The caller, from <c>GET /me</c>. A key or a service account has <see cref="Id"/>, <see cref="Kind"/> and <see cref="Name"/> only.</summary>
public sealed record MeResponse
{
    /// <summary>The id of the principal.</summary>
    public required Guid Id { get; init; }

    /// <summary>The kind of principal, such as <c>adminUser</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>The name to show.</summary>
    public required string Name { get; init; }

    /// <summary>The user name of a user.</summary>
    public string? UserName { get; init; }

    /// <summary>The email address of a user.</summary>
    public string? Email { get; init; }

    /// <summary>Whether the email address is confirmed.</summary>
    public bool EmailConfirmed { get; init; }

    /// <summary>Whether the user must change the password at the next sign-in.</summary>
    public bool MustChangePassword { get; init; }

    /// <summary>Whether two-factor sign-in is on.</summary>
    public bool TwoFactorEnabled { get; init; }

    /// <summary>The roles of a user, each in a site or in every site.</summary>
    public IReadOnlyList<UserRoleInfo> Roles { get; init; } = [];
}
