using FluentCMS.Entities.Users;
using MediatR;

namespace FluentCMS.Application.Users;
public class GetUserByIdQuery : IRequest<User?>
{
    public Guid Id { get; set; }
}
