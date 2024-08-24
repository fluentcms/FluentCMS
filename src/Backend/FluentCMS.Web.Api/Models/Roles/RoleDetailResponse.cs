namespace FluentCMS.Web.Api.Models;

public class RoleDetailResponse : BaseSiteAssociatedResponse
{
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public RoleTypes Type { get; set; }
}
