using FluentCMS.Entities;

namespace FluentCMS.Repositories;

public interface IContentTypeRepository : IGenericRepository<ContentType>
{
    Task<ContentType> GetByName(string name, CancellationToken cancellationToken = default);
}
