namespace FluentCMS.Services.Setup.Models;

public class SiteDetailModel : BaseSiteAssociatedModel
{
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public List<string> Urls { get; set; } = [];
    public Guid LayoutId { get; set; }
    public Guid DetailLayoutId { get; set; }
    public Guid EditLayoutId { get; set; }
    public List<RoleDetailModel> AdminRoles { get; set; } = [];
    public List<RoleDetailModel> ContributorRoles { get; set; } = [];
    public List<RoleDetailModel> AllRoles { get; set; } = [];
}
