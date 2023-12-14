namespace FluentCMS.Api.Models;

public class RoleCreateRequest
{
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public bool AutoAssigned { get; set; } = false;
    public Guid SiteId { get; set; }
}
