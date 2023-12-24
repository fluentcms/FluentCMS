namespace FluentCMS.Repositories.Abstractions;

public interface IContentRepository : IAppAssociatedRepository<Content>
{
    Task<IEnumerable<Content>> GetAll(Guid appId, Guid contentTypeId, CancellationToken cancellationToken = default);
}
