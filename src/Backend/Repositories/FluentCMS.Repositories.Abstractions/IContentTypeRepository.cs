namespace FluentCMS.Repositories.Abstractions;

public interface IContentTypeRepository : IAuditableEntityRepository<ContentType>
{
    Task<ContentType?> GetBySlug(string contentTypeSlug, CancellationToken cancellationToken = default);
}
