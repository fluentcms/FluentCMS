namespace FluentCMS.Web.Api.Models;

public class RoleDetailResponse : BaseAppAssociatedResponse
{
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
}
