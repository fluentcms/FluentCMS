namespace IdentityExample.DTOs.Roles;

public class RoleDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public RoleTypes Type { get; set; }
}
