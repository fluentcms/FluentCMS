namespace FluentCMS.Web.Plugins.Admin.SiteManagement;

public class SiteCreateModel
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Template { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string MetaTitle { get; set; } = string.Empty;
    public string MetaDescription { get; set; } = string.Empty;
    public string RobotsTxt { get; set; } = string.Empty;
    public string Head { get; set; } = string.Empty;
}
