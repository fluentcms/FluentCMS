namespace FluentCMS.Api.Models;

public class UserDto
{
    public required Guid Id { get; set; }
    public required string CreatedBy { get; set; }
    public required DateTime CreatedAt { get; set; }
    public required string LastUpdatedBy { get; set; }
    public required DateTime LastUpdatedAt { get; set; }

    public required string Email { get; set; }
    public required string Username { get; set; }

    public IEnumerable<Guid> RoleIds { get; set; } = [];
}
