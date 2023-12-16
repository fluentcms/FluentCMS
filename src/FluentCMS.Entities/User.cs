using Microsoft.AspNetCore.Identity;

namespace FluentCMS.Entities;

/// <summary>
/// Represents a user in the application, extending the ASP.NET Core IdentityUser class.
/// </summary>
public class User : IdentityUser<Guid>, IAuditEntity
{
    /// <summary>
    /// Gets or sets the last login date and time of the user.
    /// </summary>
    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// Gets or sets the total count of logins by the user.
    /// </summary>
    public int LoginCount { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the user last changed their password.
    /// </summary>
    public DateTime? LastPasswordChangedAt { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the user who last changed the password.
    /// </summary>
    public string LastPasswordChangedBy { get; set; } = string.Empty;

    /// <summary>
    /// Indicates whether the user is enabled or not.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <inheritdoc />
    public string CreatedBy { get; set; } = string.Empty;

    /// <inheritdoc />
    public DateTime CreatedAt { get; set; }

    /// <inheritdoc />
    public string LastUpdatedBy { get; set; } = string.Empty;

    /// <inheritdoc />
    public DateTime LastUpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the authenticator key for two-factor authentication.
    /// </summary>
    public string AuthenticatorKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the collection of claims associated with the user.
    /// </summary>
    public List<IdentityUserClaim<Guid>> Claims { get; set; } = [];

    /// <summary>
    /// Gets or sets the collection of user logins.
    /// </summary>
    public List<IdentityUserLogin<Guid>> Logins { get; set; } = [];

    /// <summary>
    /// Gets or sets the collection of tokens associated with the user.
    /// </summary>
    public List<IdentityUserToken<Guid>> Tokens { get; set; } = [];

    /// <summary>
    /// Gets or sets the collection of two-factor authentication recovery codes.
    /// </summary>
    public virtual List<UserTwoFactorRecoveryCode> RecoveryCodes { get; set; } = [];

    /// <summary>
    /// Gets or sets the collection of role IDs associated with the user.
    /// </summary>
    public List<Guid> RoleIds { get; set; } = [];

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
