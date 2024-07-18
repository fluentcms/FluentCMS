namespace FluentCMS.Repositories.MongoDB;

public class PageRowRepository : SiteAssociatedRepository<PageRow>, IPageRowRepository
{
    public PageRowRepository(IMongoDBContext mongoDbContext, IAuthContext authContext) : base(mongoDbContext, authContext)
    {
    }

    public async Task<IEnumerable<PageRow>> GetBySectionId(Guid sectionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filter = Builders<PageRow>.Filter.Eq(x => x.SectionId, sectionId);
        var result = await Collection.FindAsync(filter, cancellationToken: cancellationToken);
        return await result.ToListAsync(cancellationToken);
    }
}
