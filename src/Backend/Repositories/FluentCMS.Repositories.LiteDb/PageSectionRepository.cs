namespace FluentCMS.Repositories.LiteDb;

public class PageSectionRepository : SiteAssociatedRepository<PageSection>, IPageSectionRepository
{
    public PageSectionRepository(ILiteDBContext liteDbContext, IAuthContext authContext) : base(liteDbContext, authContext)
    {
    }

    public async Task<IEnumerable<PageSection>> GetByPageId(Guid pageId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Collection.Query().Where(x => x.PageId == pageId).ToEnumerableAsync();
    }
}
