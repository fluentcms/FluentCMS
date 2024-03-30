namespace FluentCMS.Repositories.Abstractions;

public interface IContentRepository : IAuditableEntityRepository<Content>
{
    Task<IEnumerable<Content>> GetAll(Guid contentTypeId, CancellationToken cancellationToken = default);
}
