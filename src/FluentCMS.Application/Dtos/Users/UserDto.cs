namespace FluentCMS.Application.Dtos.Users;
public class UserDto
{
    public required Guid Id { get; set; }
    public required string CreatedBy { get; set; }
    public required DateTime CreatedAt { get; set; }
    public required string LastUpdatedBy { get; set; }
    public required DateTime LastUpdatedAt { get; set; }

    public string Name { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;

    public virtual IEnumerable<Guid> UserRoles { get; set; } = Enumerable.Empty<Guid>();
}
