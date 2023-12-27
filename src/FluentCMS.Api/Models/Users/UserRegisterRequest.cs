namespace FluentCMS.Api.Models;

/// <summary>
/// Represents a request for registering a new user.
/// </summary>
public class UserRegisterRequest
{
    /// <summary>
    /// Gets or sets the email address of the user being registered.
    /// </summary>
    /// <value>
    /// The email address of the user.
    /// </value>
    public required string Email { get; set; }

    /// <summary>
    /// Gets or sets the username for the user being registered.
    /// </summary>
    /// <value>
    /// The username for the user.
    /// </value>
    public required string Username { get; set; }

    /// <summary>
    /// Gets or sets the password for the user being registered.
    /// </summary>
    /// <value>
    /// The password for the user.
    /// </value>
    public required string Password { get; set; }
}
