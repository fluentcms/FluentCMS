using MediatR;

namespace FluentCMS.Application.Users;
public class CreateUserCommand : IRequest<Guid>
{
    public string Name { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public ICollection<Guid> Roles { get; set; } = new List<Guid>();
}
