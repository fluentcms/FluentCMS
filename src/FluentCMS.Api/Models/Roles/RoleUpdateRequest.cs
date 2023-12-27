namespace FluentCMS.Api.Models;

/// <summary>
/// Represents a request to update an existing role.
/// </summary>
public class RoleUpdateRequest : RoleCreateRequest
{
    /// <summary>
    /// Gets or sets the unique identifier of the role to be updated.
    /// </summary>
    /// <value>
    /// The unique identifier of the role.
    /// </value>
    public Guid Id { get; set; }
}
