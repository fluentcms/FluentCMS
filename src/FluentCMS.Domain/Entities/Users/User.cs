namespace FluentCMS.Entities.Users;

public class User : IAuditEntity
{
    public required Guid Id { get; set; }
    public required string CreatedBy { get; set; }
    public required DateTime CreatedAt { get; set; }
    public required string LastUpdatedBy { get; set; }
    public required DateTime LastUpdatedAt { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    public virtual ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
}
