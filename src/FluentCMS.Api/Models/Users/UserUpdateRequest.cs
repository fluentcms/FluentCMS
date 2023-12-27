namespace FluentCMS.Api.Models;

/// <summary>
/// Represents a request for updating an existing user's details.
/// </summary>
public class UserUpdateRequest
{
    /// <summary>
    /// Gets or sets the unique identifier of the user to be updated.
    /// </summary>
    /// <value>
    /// The unique identifier of the user.
    /// </value>
    public required Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the new email address for the user.
    /// </summary>
    /// <value>
    /// The new email address to be set for the user.
    /// </value>
    public required string Email { get; set; }

    /// <summary>
    /// Gets or sets the collection of role identifiers to be associated with the user.
    /// </summary>
    /// <value>
    /// A collection of unique identifiers for the roles to be assigned to the user.
    /// </value>
    public ICollection<Guid> RoleIds { get; set; } = new List<Guid>();
}
