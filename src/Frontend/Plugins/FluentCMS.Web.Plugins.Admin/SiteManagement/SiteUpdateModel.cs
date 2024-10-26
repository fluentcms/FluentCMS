namespace FluentCMS.Web.Plugins.Admin.SiteManagement;

public class SiteUpdateModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public string Description { get; set; } = default!;
    public Guid LayoutId { get; set; }
    public Guid DetailLayoutId { get; set; }
    public ICollection<Guid> AdminRoleIds { get; set; } = [];
    public ICollection<Guid> ContributorRoleIds { get; set; } = [];
    public string MetaTitle { get; set; } = default!;
    public string MetaDescription { get; set; } = default!;
    public string Robots { get; set; } = default!;
    public string OgType { get; set; } = default!;
    public string GoogleTagsId { get; set; } = default!;
    public string Head { get; set; } = default!;
    // public Guid Favicon { get; set; } = default!;
    // public Guid SocialImage { get; set; } = default!;
    public string Urls { get; set; } = default!; // comma separated list
}
