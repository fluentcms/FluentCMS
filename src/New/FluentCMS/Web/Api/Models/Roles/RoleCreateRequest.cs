namespace FluentCMS.Web.Api.Models;

public class RoleCreateRequest
{
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
}
