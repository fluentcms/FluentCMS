namespace FluentCMS.Web.Api.Models;

/// <summary>
/// Represents a request for changing a user's password.
/// </summary>
public class UserChangePasswordRequest
{
    /// <summary>
    /// Gets or sets the unique identifier of the user whose password is to be changed.
    /// </summary>
    /// <value>
    /// The unique identifier of the user.
    /// </value>
    public required Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the current (old) password of the user.
    /// </summary>
    /// <value>
    /// The current password of the user.
    /// </value>
    public required string OldPassword { get; set; }

    /// <summary>
    /// Gets or sets the new password for the user.
    /// </summary>
    /// <value>
    /// The new password to be set for the user.
    /// </value>
    public required string NewPassword { get; set; }
}
