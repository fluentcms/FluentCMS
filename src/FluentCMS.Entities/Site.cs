namespace FluentCMS.Entities;

/// <summary>
/// Represents a website, including its basic information and configuration.
/// Inherits from SiteAssociatedEntity, consider reviewing this inheritance for correctness.
/// </summary>
public class Site : SiteAssociatedEntity
{
    /// <summary>
    /// Name of the site. This field is required.
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Optional description of the site.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// List of URLs associated with the site.
    /// </summary>
    public List<string> Urls { get; set; } = [];

    /// <summary>
    /// Identifier of the default layout for the site.
    /// </summary>
    public Guid DefaultLayoutId { get; set; }
}
