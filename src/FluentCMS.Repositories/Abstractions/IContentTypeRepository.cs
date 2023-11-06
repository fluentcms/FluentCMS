using FluentCMS.Entities.ContentTypes;

namespace FluentCMS.Repositories.Abstractions;

public interface IContentTypeRepository : IGenericRepository<ContentType>
{
    Task<ContentType?> GetBySlug(string slug, CancellationToken cancellationToken = default);
    Task<bool> SlugExists(string slug, Guid exceptId, CancellationToken cancellationToken = default);
}
