namespace FluentCMS.Admin.Api;

/// <summary>A role of a user, as <see cref="MeResponse.Roles"/> lists it.</summary>
public sealed record UserRoleInfo
{
    /// <summary>The id of the role.</summary>
    public required Guid Id { get; init; }

    /// <summary>The name of the role.</summary>
    public required string Name { get; init; }

    /// <summary>The site the role is held in, or <see langword="null"/> for every site.</summary>
    public Guid? SiteId { get; init; }
}
