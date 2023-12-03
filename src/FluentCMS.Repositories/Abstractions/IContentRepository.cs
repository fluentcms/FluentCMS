using FluentCMS.Entities;

namespace FluentCMS.Repositories;

public interface IContentRepository
{
    Task<Content?> Create(Content content, CancellationToken cancellationToken = default);
    Task<Content?> Update(Content content, CancellationToken cancellationToken = default);
    Task<Content?> Delete(string contentType, Guid id, CancellationToken cancellationToken = default);

    Task<IEnumerable<Content>> GetAll(string contentType, CancellationToken cancellationToken = default);
    Task<Content?> GetById(string contentType, Guid id, CancellationToken cancellationToken = default);
}
