using FluentCMS.Entities.Users;
using MediatR;

namespace FluentCMS.Application.Users;
public class GetUsersQuery : IRequest<IEnumerable<User>>
{
}
