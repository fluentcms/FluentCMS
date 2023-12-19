using FluentCMS.Entities;

namespace FluentCMS.Repositories;

public interface IContentRepository<TContent> : IAppAssociatedRepository<TContent> where TContent : Content, new()
{
    Task<IEnumerable<TContent>> GetAll(Guid appId, Guid contentTypeId, CancellationToken cancellationToken = default);
}
