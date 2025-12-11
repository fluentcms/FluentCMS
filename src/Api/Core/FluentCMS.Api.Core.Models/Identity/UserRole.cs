namespace FluentCMS.Api.Core.Models.Identity;

public class UserRole : IdentityUserRole<Guid>
{
    // IAuditableEntity implementations
    public Guid Id { get; set; }
    public string? CreatedBy { get; set; } = default!;
    public DateTime CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int Version { get; set; }
}
