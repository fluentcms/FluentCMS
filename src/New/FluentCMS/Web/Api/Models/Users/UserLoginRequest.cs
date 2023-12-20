namespace FluentCMS.Web.Api.Models;

/// <summary>
/// Represents a request for user login.
/// </summary>
public class UserLoginRequest
{
    /// <summary>
    /// Gets or sets the username of the user attempting to log in.
    /// </summary>
    /// <value>
    /// The username of the user.
    /// </value>
    public required string Username { get; set; }

    /// <summary>
    /// Gets or sets the password of the user attempting to log in.
    /// </summary>
    /// <value>
    /// The password of the user.
    /// </value>
    public required string Password { get; set; }
}
