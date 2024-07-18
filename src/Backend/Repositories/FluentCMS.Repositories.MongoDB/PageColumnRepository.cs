namespace FluentCMS.Repositories.MongoDB;

public class PageColumnRepository : SiteAssociatedRepository<PageColumn>, IPageColumnRepository
{
    public PageColumnRepository(IMongoDBContext mongoDbContext, IAuthContext authContext) : base(mongoDbContext, authContext)
    {
    }

    public async Task<IEnumerable<PageColumn>> GetByRowId(Guid rowId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filter = Builders<PageColumn>.Filter.Eq(x => x.RowId, rowId);
        var result = await Collection.FindAsync(filter, cancellationToken: cancellationToken);
        return await result.ToListAsync(cancellationToken);
    }
}
