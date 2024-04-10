using System.ComponentModel.DataAnnotations;
using static FluentCMS.Web.Api.Models.UserRegisterRequest;

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
    [Required]
    public required Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the current (old) password of the user.
    /// </summary>
    /// <value>
    /// The current password of the user.
    /// </value>
    [Required]
    public required string OldPassword { get; set; }

    /// <summary>
    /// Gets or sets the new password for the user.
    /// </summary>
    /// <value>
    /// The new password to be set for the user.
    /// </value>
    [Required]
    [PasswordValidator]
    public required string NewPassword { get; set; }
}
