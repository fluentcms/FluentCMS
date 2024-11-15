namespace FluentCMS.Repositories.EFCore;

public class ContentTypeRepository(FluentCmsDbContext dbContext, IApiExecutionContext apiExecutionContext) : SiteAssociatedRepository<ContentType>(dbContext, apiExecutionContext), IContentTypeRepository
{
    public async Task<ContentType?> GetBySlug(Guid siteId, string contentTypeSlug, CancellationToken cancellationToken = default)
    {
        return await DbContext.ContentTypes.SingleOrDefaultAsync(x => x.SiteId == siteId && x.Slug == contentTypeSlug, cancellationToken);
    }
}
