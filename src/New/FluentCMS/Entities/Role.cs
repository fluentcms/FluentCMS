using Microsoft.AspNetCore.Identity;

namespace FluentCMS.Entities;

public class Role : IdentityRole<Guid>, IAppAssociatedEntity
{
    public string? Description { get; set; }
    public string CreatedBy { get; set; } = default!;
    public DateTime CreatedAt { get; set; }
    public string? ModifiedBy { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public Guid AppId { get; set; }
}
