namespace FluentCMS.Repositories.MongoDB;

public class PageSectionRepository : SiteAssociatedRepository<PageSection>, IPageSectionRepository
{
    public PageSectionRepository(IMongoDBContext mongoDbContext, IAuthContext authContext) : base(mongoDbContext, authContext)
    {
    }

    public async Task<IEnumerable<PageSection>> GetByPageId(Guid pageId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filter = Builders<PageSection>.Filter.Eq(x => x.PageId, pageId);
        var result = await Collection.FindAsync(filter, cancellationToken: cancellationToken);
        return await result.ToListAsync(cancellationToken);
    }
}
