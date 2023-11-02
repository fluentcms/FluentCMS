namespace FluentCMS.Application.Dtos.Users;
public class EditRoleRequest
{
    public required Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required bool AutoAssigned { get; set; }
    public Guid? SiteId { get; set; }
}
