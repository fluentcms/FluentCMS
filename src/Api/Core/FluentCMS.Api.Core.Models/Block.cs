namespace FluentCMS.Api.Core.Models;

public class Block : SiteAssociatedEntity
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Content { get; set; } = string.Empty;
}