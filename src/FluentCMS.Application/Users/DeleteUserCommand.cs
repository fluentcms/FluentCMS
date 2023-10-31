using MediatR;

namespace FluentCMS.Application.Users;
public class DeleteUserCommand : IRequest
{
    public Guid Id { get; set; }
}
