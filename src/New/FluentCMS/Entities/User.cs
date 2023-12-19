using Microsoft.AspNetCore.Identity;

namespace FluentCMS.Entities;

/// <summary>
/// Represents a user in the application, extending the ASP.NET Core IdentityUser class.
/// Inherits from <see cref="IdentityUser{TKey}"/> to leverage ASP.NET Core Identity.
/// Extends <see cref="IAuditableEntity"/> to track audit information.
/// </summary>
public class User : IdentityUser<Guid>, IAuditableEntity
{
    /// <summary>
    /// Gets or sets the last login date and time of the user.
    /// </summary>
    public DateTime? LoginAt { get; set; }

    /// <summary>
    /// Gets or sets the total count of logins by the user.
    /// </summary>
    public int LoginCount { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the user last changed their password.
    /// </summary>
    public DateTime? PasswordChangedAt { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the user who last changed the password.
    /// </summary>
    public string PasswordChangedBy { get; set; } = string.Empty;

    /// <summary>
    /// Indicates whether the user is enabled or not.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <inheritdoc />
    public string CreatedBy { get; set; } = string.Empty;

    /// <inheritdoc />
    public DateTime CreatedAt { get; set; }

    /// <inheritdoc />
    public string? ModifiedBy { get; set; }

    /// <inheritdoc />
    public DateTime? ModifiedAt { get; set; }

    /// <summary>
    /// Gets or sets the collection of role names (normalized role name) associated with the user.
    /// </summary>
    public List<string> Roles { get; set; } = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="User"/> class.
    /// </summary>
    public User()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="User"/> class with the specified username.
    /// </summary>
    /// <param name="userName">The username for the new user.</param>
    public User(string userName) : base(userName)
    {
    }
}
