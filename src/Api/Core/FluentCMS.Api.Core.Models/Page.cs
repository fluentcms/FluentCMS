namespace FluentCMS.Api.Core.Models;

public class Page : SiteAssociatedEntity
{
    public string Title { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public int Order { get; set; }
    public Guid SiteId { get; set; }
    public Guid? ParentId { get; set; }
    public Guid? LayoutId { get; set; }
    public Guid? EditLayoutId { get; set; }
    public Guid? DetailLayoutId { get; set; }
    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
    public bool? RobotsIndex { get; set; }
    public bool? RobotsFollow { get; set; }
    public string? OgType { get; set; }
    public string? Head { get; set; }    
    // public bool Locked { get; set; } = false;
}