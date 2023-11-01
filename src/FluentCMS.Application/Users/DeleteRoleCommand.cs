using MediatR;

namespace FluentCMS.Application.Users;
public class DeleteRoleCommand : IRequest
{
    public required Guid Id { get; set; }
}
