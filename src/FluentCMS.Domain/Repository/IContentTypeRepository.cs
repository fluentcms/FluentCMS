using FluentCMS.Entities.ContentTypes;

namespace FluentCMS.Repository;

public interface IContentTypeRepository
    : IGenericRepository<ContentType>
{
    Task<ContentType?> GetBySlug(string slug);
}
