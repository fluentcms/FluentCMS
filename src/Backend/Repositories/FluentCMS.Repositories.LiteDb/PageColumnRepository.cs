namespace FluentCMS.Repositories.LiteDb;

public class PageColumnRepository : SiteAssociatedRepository<PageColumn>, IPageColumnRepository
{
    public PageColumnRepository(ILiteDBContext liteDbContext, IAuthContext authContext) : base(liteDbContext, authContext)
    {
    }

    public async Task<IEnumerable<PageColumn>> GetByRowId(Guid rowId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Collection.Query().Where(x => x.RowId == rowId).ToEnumerableAsync();
    }
}
