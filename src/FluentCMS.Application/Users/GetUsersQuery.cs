using FluentCMS.Entities;
using MediatR;

namespace FluentCMS.Application.Users;
public class GetUsersQuery : IRequest<IEnumerable<User>>
{
}
