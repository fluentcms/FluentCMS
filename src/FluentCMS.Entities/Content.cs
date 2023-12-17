namespace FluentCMS.Entities;

/// <summary>
/// Represents a content entity associated with a site in the FluentCMS system.
/// This class holds information about the content's type and its associated values.
/// </summary>
public class Content : SiteAssociatedEntity
{
    /// <summary>
    /// Gets or sets the type of the content. This field is used to categorize
    /// the content within the FluentCMS system.
    /// </summary>
    /// <value>
    /// A string that represents the content type.
    /// </value>
    public string Type { get; set; } = default!;

    /// <summary>
    /// Gets or sets a collection of key-value pairs that represent the content's data.
    /// Each entry in the dictionary holds a piece of content data, where the key is a
    /// string that describes the data, and the value is the data itself.
    /// </summary>
    /// <value>
    /// A dictionary with string keys and values of type object, which can hold any data type.
    /// </value>
    public Dictionary<string, object?> Value { get; set; } = [];
}
