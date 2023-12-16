namespace FluentCMS.Api.Models;

public class RoleDto
{
    public Guid Id { get; set; }
    public string CreatedBy { get; set; } = default!;
    public DateTime CreatedAt { get; set; }
    public string LastUpdatedBy { get; set; } = default!;
    public DateTime LastUpdatedAt { get; set; }

    public Guid SiteId { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; } = default!;
}
