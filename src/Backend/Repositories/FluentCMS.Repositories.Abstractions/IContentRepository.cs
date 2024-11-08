namespace FluentCMS.Repositories.Abstractions;

public interface IContentRepository : ISiteAssociatedRepository<Content>
{
    Task<IEnumerable<Content>> GetAll(Guid contentTypeId, CancellationToken cancellationToken = default);
}
