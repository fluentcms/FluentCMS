namespace FluentCMS.Repositories.EFCore;

public class ContentRepository(FluentCmsDbContext dbContext, IApiExecutionContext apiExecutionContext) : SiteAssociatedRepository<Content>(dbContext, apiExecutionContext),
    IContentRepository
{

    public async Task<IEnumerable<Content>> GetAll(Guid contentTypeId, CancellationToken cancellationToken = default)
    {
        return await DbContext.Contents.Where(x => x.TypeId == contentTypeId).ToListAsync(cancellationToken);
    }
}
