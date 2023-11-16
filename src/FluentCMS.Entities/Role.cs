using Microsoft.AspNetCore.Identity;

namespace FluentCMS.Entities;

public class Role : IdentityRole<Guid>, IAuditEntity
{
    public string CreatedBy { get; set; } = default!;
    public DateTime CreatedAt { get; set; }
    public string LastUpdatedBy { get; set; } = default!;
    public DateTime LastUpdatedAt { get; set; }

    public Guid SiteId { get; set; }
    public string? Description { get; set; }

    public Role()
    {
    }

    public Role(string name) : base(name)
    {
    }

    public override string ToString()
    {
        return NormalizedName ?? string.Empty;
    }
}
