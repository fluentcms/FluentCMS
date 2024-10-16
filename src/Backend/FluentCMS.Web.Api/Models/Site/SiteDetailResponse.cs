namespace FluentCMS.Web.Api.Models;

public class SiteDetailResponse : BaseAuditableResponse
{
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public List<string> Urls { get; set; } = [];
    public Guid LayoutId { get; set; }
    public Guid DetailLayoutId { get; set; }
    public Guid EditLayoutId { get; set; }
    public List<Guid> AdminRoleIds { get; set; } = [];
    public List<Guid> ContributorRoleIds { get; set; } = [];
    public List<RoleDetailResponse> AllRoles { get; set; } = [];
}
