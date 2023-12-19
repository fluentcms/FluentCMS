using FluentCMS.Entities;

namespace FluentCMS.Repositories;

public interface IUserRepository : IAuditableEntityRepository<User>, IQueryableRepository<User>
{
}
