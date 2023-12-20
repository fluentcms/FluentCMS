namespace FluentCMS.Web.Api.Models;

public class RoleResponse : BaseAppAssociatedResponse
{
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
}
