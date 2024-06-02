namespace FluentCMS.Web.Api.Models;

public class RoleDetailResponse : BaseAuditableResponse
{
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public ICollection<Policy> Policies { get; set; } = [];
}
