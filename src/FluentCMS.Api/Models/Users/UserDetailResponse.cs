namespace FluentCMS.Api.Models;

/// <summary>
/// Represents the response details of a user.
/// </summary>
public class UserDetailResponse
{
    /// <summary>
    /// Gets or sets the unique identifier of the user.
    /// </summary>
    /// <value>
    /// The unique identifier of the user.
    /// </value>
    public required Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the user who created this user record.
    /// </summary>
    /// <value>
    /// The identifier of the user who created this user record.
    /// </value>
    public required string CreatedBy { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the user was created.
    /// </summary>
    /// <value>
    /// The date and time of user creation.
    /// </value>
    public required DateTime CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the user who last updated this user record.
    /// </summary>
    /// <value>
    /// The identifier of the user who last updated this user record.
    /// </value>
    public required string LastUpdatedBy { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the user was last updated.
    /// </summary>
    /// <value>
    /// The date and time of the last update to the user record.
    /// </value>
    public required DateTime LastUpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the email address of the user.
    /// </summary>
    /// <value>
    /// The email address of the user.
    /// </value>
    public required string Email { get; set; }

    /// <summary>
    /// Gets or sets the username of the user.
    /// </summary>
    /// <value>
    /// The username of the user.
    /// </value>
    public required string Username { get; set; }

    /// <summary>
    /// Gets or sets the date and time of the user's last login.
    /// </summary>
    /// <value>
    /// The date and time of the last login, or null if the user has never logged in.
    /// </value>
    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// Gets or sets the total count of logins by the user.
    /// </summary>
    /// <value>
    /// The total number of times the user has logged in.
    /// </value>
    public int LoginCount { get; set; }
}
