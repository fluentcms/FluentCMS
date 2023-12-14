using FluentCMS.Entities;

namespace FluentCMS.Repositories;

public interface IContentTypeRepository : IGenericRepository<ContentType>
{
    Task<ContentType?> GetByName(string name, CancellationToken cancellationToken = default);
    Task<ContentType?> AddField(Guid contentTypeId, ContentTypeField field, CancellationToken cancellationToken = default);
    Task<ContentType?> RemoveField(Guid contentTypeId, string fieldName, CancellationToken cancellationToken = default);
    Task<ContentType?> UpdateField(Guid contentTypeId, ContentTypeField field, CancellationToken cancellationToken = default);
}
