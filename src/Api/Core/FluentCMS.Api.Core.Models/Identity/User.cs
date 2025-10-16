namespace FluentCMS.Api.Core.Models.Identity;

public class User : IdentityUser<Guid>, IAuditableEntity
{
    // IAuditableEntity implementations
    public string? CreatedBy { get; set; } = default!;
    public DateTime CreatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int Version { get; set; }

    // Additional properties

    public bool IsSuperAdmin { get; set; } = false;
    public DateTime? LastLogin { get; set; }
    public int LoginCount { get; set; }
    public DateTime? PasswordChangedAt { get; set; }
    public string? PasswordChangedBy { get; set; }
    public bool Enabled { get; set; } = true;
    public string Description { get; set; } = string.Empty;

    public User()
    {
    }

    public User(string userName) : base(userName)
    {
    }
}
