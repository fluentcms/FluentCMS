using FluentCMS.Entities;

namespace FluentCMS.Repositories;

public interface IContentRepository<TContent> where TContent : Content, new()
{
    Task<TContent?> Create(TContent content, CancellationToken cancellationToken = default);
    Task<TContent?> Update(TContent content, CancellationToken cancellationToken = default);
    Task<TContent?> Delete(Guid siteId, string contentType, Guid id, CancellationToken cancellationToken = default);

    Task<IEnumerable<TContent>> GetAll(Guid siteId, string contentType, CancellationToken cancellationToken = default);
    Task<TContent?> GetById(Guid siteId, string contentType, Guid id, CancellationToken cancellationToken = default);
}
