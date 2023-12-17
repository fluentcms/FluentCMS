namespace FluentCMS.Api.Models;

/// <summary>
/// Represents a request for creating a new content entity in the FluentCMS system.
/// This class serves as a base for content creation requests, providing essential properties required for this operation.
/// </summary>
public class ContentCreateRequest
{
    /// <summary>
    /// Gets or sets the unique identifier of the site associated with the new content.
    /// </summary>
    /// <value>
    /// The Guid representing the unique identifier of the site.
    /// </value>
    public required Guid SiteId { get; set; }

    /// <summary>
    /// Gets or sets the values associated with the content. 
    /// This dictionary holds key-value pairs representing various attributes of the content.
    /// </summary>
    /// <value>
    /// A dictionary with string keys and object values, allowing for storage of various types of content data.
    /// </value>
    public required Dictionary<string, object?> Value { get; set; }
}
