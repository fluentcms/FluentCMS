namespace FluentCMS.Repositories.Abstractions;

public interface IContentTypeRepository : IAppAssociatedRepository<ContentType>
{
    Task<ContentType?> GetBySlug(Guid appId, string contentTypeSlug, CancellationToken cancellationToken = default);
}
