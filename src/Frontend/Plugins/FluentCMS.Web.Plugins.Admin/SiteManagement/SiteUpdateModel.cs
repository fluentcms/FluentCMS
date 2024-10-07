namespace FluentCMS.Web.Plugins.Admin.SiteManagement;

public class SiteUpdateModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public string Description { get; set; } = default!;
    public Guid LayoutId { get; set; }
    public Guid DetailLayoutId { get; set; }
    public Guid EditLayoutId { get; set; }
    public string MetaTitle { get; set; } = default!;
    public string MetaDescription { get; set; } = default!;
    public string MetaKeywords { get; set; } = default!;
    public string Urls { get; set; } = default!; // comma separated list
}
