namespace FluentCMS.Repositories.EFCore;

public class ContentRepository(FluentCmsDbContext dbContext, IMapper mapper, IApiExecutionContext apiExecutionContext) : SiteAssociatedRepository<Content, ContentModel>(dbContext, mapper, apiExecutionContext),
    IContentRepository
{

    public async Task<IEnumerable<Content>> GetAll(Guid contentTypeId, CancellationToken cancellationToken = default)
    {
        var dbEntity = await DbContext.Contents.Where(x => x.ContentTypeId == contentTypeId).ToListAsync(cancellationToken);
        return Mapper.Map<IEnumerable<Content>>(dbEntity);
    }
}
