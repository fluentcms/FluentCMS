using FluentCMS.Entities;
using MediatR;

namespace FluentCMS.Application.Users;
public class GetUserByIdQuery : IRequest<User?>
{
    public Guid Id { get; set; }
}
