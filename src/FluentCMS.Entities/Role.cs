using Microsoft.AspNetCore.Identity;

namespace FluentCMS.Entities;

public class Role : IdentityRole<Guid>, IAuditEntity
{
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string LastUpdatedBy { get; set; } = string.Empty;
    public DateTime LastUpdatedAt { get; set; }
    public Guid SiteId { get; set; }
    public required bool AutoAssigned { get; set; }
    public string Description { get; set; } = string.Empty;
    public List<IdentityRoleClaim<Guid>> Claims { get; set; } = [];

    public Role()
    {
    }

    public Role(string name) : base(name)
    {
    }

    public override string ToString()
    {
        return Name ?? string.Empty;
    }
}
