using FluentCMS.Entities;

namespace FluentCMS.Repository;

public interface IUserRepository
    : IGenericRepository<User>
{
    Task<User?> GetByUsername(string username);
}
