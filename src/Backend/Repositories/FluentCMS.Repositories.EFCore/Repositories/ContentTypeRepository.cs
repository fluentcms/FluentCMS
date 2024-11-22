namespace FluentCMS.Repositories.EFCore;

public class ContentTypeRepository(FluentCmsDbContext dbContext, IMapper mapper, IApiExecutionContext apiExecutionContext) : SiteAssociatedRepository<ContentType, ContentTypeModel>(dbContext, mapper, apiExecutionContext), IContentTypeRepository
{
    public async Task<ContentType?> GetBySlug(Guid siteId, string contentTypeSlug, CancellationToken cancellationToken = default)
    {
        var dbEntities = await DbContext.Set<ContentType>().FirstOrDefaultAsync(x => x.SiteId == siteId && x.Slug == contentTypeSlug, cancellationToken);
        return Mapper.Map<ContentType>(dbEntities);
    }
}
