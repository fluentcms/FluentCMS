using FluentCMS.Entities.Users;
using MediatR;

namespace FluentCMS.Application.Users;
public class GetRolesQuery : IRequest<IEnumerable<Role>>
{
}
