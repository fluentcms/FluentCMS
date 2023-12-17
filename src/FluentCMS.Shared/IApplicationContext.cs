namespace FluentCMS;

/// <summary>
/// Represents the application context interface.
/// </summary>
public interface IApplicationContext
{
    /// <summary>
    /// Gets or sets the current context.
    /// </summary>
    ICurrentContext Current { get; set; }
}

/// <summary>
/// Represents the current user context interface.
/// </summary>
public interface ICurrentContext
{
    /// <summary>
    /// Gets or sets the role IDs associated with the current user.
    /// </summary>
    IEnumerable<Guid> RoleIds { get; set; }

    /// <summary>
    /// Gets or sets the username of the current user.
    /// </summary>
    string Username { get; set; }

    /// <summary>
    /// Gets a value indicating whether the user is authenticated.
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the current user is a super admin.
    /// </summary>
    bool IsSuperAdmin { get; set; }
}
