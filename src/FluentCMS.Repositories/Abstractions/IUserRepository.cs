using FluentCMS.Entities;

namespace FluentCMS.Repositories.Abstractions;

public interface IUserRepository : IGenericRepository<User>
{
    Task<User?> GetByUsername(string username, CancellationToken cancellationToken = default);
}
