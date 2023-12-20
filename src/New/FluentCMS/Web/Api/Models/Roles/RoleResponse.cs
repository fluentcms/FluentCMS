namespace FluentCMS.Web.Api.Models;

public class RoleResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
}
