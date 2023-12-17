namespace FluentCMS.Api.Models;

/// <summary>
/// Represents the response returned after a user login request.
/// </summary>
public class UserLoginResponse
{
    /// <summary>
    /// Gets or sets the unique identifier of the user.
    /// </summary>
    /// <value>
    /// The unique identifier of the user who has logged in.
    /// </value>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the list of role identifiers associated with the user.
    /// </summary>
    /// <value>
    /// A list of unique identifiers for the roles assigned to the user.
    /// </value>
    public List<Guid> RoleIds { get; set; } = [];

    /// <summary>
    /// Gets or sets the authentication token for the session.
    /// </summary>
    /// <value>
    /// The token used for authenticating subsequent requests.
    /// </value>
    public required string Token { get; set; }
}
