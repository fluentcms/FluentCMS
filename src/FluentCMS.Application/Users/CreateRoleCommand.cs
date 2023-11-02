using MediatR;

namespace FluentCMS.Application.Users;
public class CreateRoleCommand : IRequest<Guid>
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool AutoAssigned { get; set; }
    public Guid? SiteId { get; set; }
}
