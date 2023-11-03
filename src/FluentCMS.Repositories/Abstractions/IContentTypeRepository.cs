using FluentCMS.Entities;

namespace FluentCMS.Repositories.Abstractions;

public interface IContentTypeRepository : IGenericRepository<ContentType>
{
    Task<ContentType?> GetBySlug(string slug, CancellationToken cancellationToken = default);
}
