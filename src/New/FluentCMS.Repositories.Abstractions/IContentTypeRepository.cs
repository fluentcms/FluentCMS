using FluentCMS.Entities;

namespace FluentCMS.Repositories;

public interface IContentTypeRepository : IAppAssociatedRepository<ContentType>
{
    Task<ContentType?> GetBySlug(Guid appId, string contentTypeSlug, CancellationToken cancellationToken = default);
    Task<ContentType?> AddField(Guid contentTypeId, ContentTypeField field, CancellationToken cancellationToken = default);
    Task<ContentType?> RemoveField(Guid contentTypeId, string fieldSlug, CancellationToken cancellationToken = default);
    Task<ContentType?> UpdateField(Guid contentTypeId, ContentTypeField field, CancellationToken cancellationToken = default);
}
