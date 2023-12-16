namespace FluentCMS.Entities;

public class Content : Dictionary<string, object?>, ISiteAssociatedEntity
{
    public Guid Id { get; set; } = default!;
    public string CreatedBy { get; set; } = string.Empty; // UserName
    public DateTime CreatedAt { get; set; }
    public string LastUpdatedBy { get; set; } = string.Empty; // UserName
    public DateTime LastUpdatedAt { get; set; }
    public string Type { get; set; } = string.Empty;
    public Guid SiteId { get; set; }
}
