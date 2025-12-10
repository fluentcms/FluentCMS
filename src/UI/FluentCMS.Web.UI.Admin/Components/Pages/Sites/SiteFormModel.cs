namespace FluentCMS.Web.UI.Admin;

public class SiteFormModel
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Urls { get; set; }
    public string? Head { get; set; }
    public Guid? LayoutId { get; set; }
    public Guid? EditLayoutId { get; set; }
    public Guid? DetailLayoutId { get; set; }
    public string? OgType { get; set; }
    public string? GoogleTagsId { get; set; }
    public string? MetaTitle { get; set; }
    public string? MetaDescription { get; set; }
    public bool RobotsIndex { get; set; }
    public bool RobotsFollow { get; set; }
    public string? RobotsTxt { get; set; }
}
