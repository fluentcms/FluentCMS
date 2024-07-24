namespace FluentCMS.Repositories.LiteDb;

public class PageColumnRepository : SiteAssociatedRepository<PageColumn>, IPageColumnRepository
{
    public PageColumnRepository(ILiteDBContext liteDbContext, IAuthContext authContext) : base(liteDbContext, authContext)
    {
    }

    public async Task<IEnumerable<PageColumn>> GetByColumnId(Guid columnId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Collection.Query().Where(x => x.ColumnId == columnId).ToEnumerableAsync();
    }
}
