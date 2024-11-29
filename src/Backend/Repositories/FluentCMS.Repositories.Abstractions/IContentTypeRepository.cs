namespace FluentCMS.Repositories.Abstractions;

public interface IContentTypeRepository : ISiteAssociatedRepository<ContentType>
{
    Task<ContentType?> GetBySlug(Guid siteId, string contentTypeSlug, CancellationToken cancellationToken = default);
}
