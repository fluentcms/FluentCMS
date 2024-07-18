namespace FluentCMS.Repositories.LiteDb;

public class PageRowRepository : SiteAssociatedRepository<PageRow>, IPageRowRepository
{
    public PageRowRepository(ILiteDBContext liteDbContext, IAuthContext authContext) : base(liteDbContext, authContext)
    {
    }

    public async Task<IEnumerable<PageRow>> GetBySectionId(Guid sectionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Collection.Query().Where(x => x.SectionId == sectionId).ToEnumerableAsync();
    }
}
