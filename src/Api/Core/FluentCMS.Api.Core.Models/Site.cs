namespace FluentCMS.Api.Core.Models;

public class Site : AuditableEntity
{
    public string Name { get; set; } = default!;
    public List<string> Urls { get; set; } = [];
    public string? Description { get; set; } 
    public Guid? LayoutId { get; set; }
    public Guid? DetailLayoutId { get; set; }
    public Guid? EditLayoutId { get; set; }
    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
    public bool? RobotsIndex { get; set; }
    public bool? RobotsFollow { get; set; }
    public string? RobotsTxt { get; set; }
    public string? GoogleTagsId { get; set; }
    public string? OgType { get; set; }
    public string? Head { get; set; }    
}
