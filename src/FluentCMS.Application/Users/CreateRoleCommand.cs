using MediatR;

namespace FluentCMS.Application.Users;
public class CreateRoleCommand : IRequest<Guid>
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required bool AutoAssigned { get; set; }
    public Guid? SiteId { get; set; }
}
