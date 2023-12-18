namespace FluentCMS.Api.Models;

/// <summary>
/// Represents a request for updating an existing content entity in the FluentCMS system.
/// This class extends <see cref="ContentCreateRequest"/> with an additional property for the content's unique identifier.
/// </summary>
public class ContentUpdateRequest : ContentCreateRequest
{
    /// <summary>
    /// Gets or sets the unique identifier of the content entity to be updated.
    /// </summary>
    /// <value>
    /// The Guid representing the unique identifier of the content entity.
    /// </value>
    public Guid Id { get; set; }
}
