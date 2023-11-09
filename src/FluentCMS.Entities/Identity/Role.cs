using Microsoft.AspNetCore.Identity;

namespace FluentCMS.Entities.Identity;

public class Role : IdentityRole<Guid>, IAuditEntity
{
    public Guid SiteId { get; set; }

    public string Description { get; set; } = string.Empty;
    public List<IdentityRoleClaim<Guid>> Claims { get; set; } = [];

    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public string LastUpdatedBy { get; set; } = string.Empty;
    public DateTime LastUpdatedAt { get; set; }

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
