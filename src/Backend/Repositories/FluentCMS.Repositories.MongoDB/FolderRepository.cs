namespace FluentCMS.Repositories.MongoDB;

public class FolderRepository(IMongoDBContext mongoDbContext, IApiExecutionContext apiExecutionContext) : SiteAssociatedRepository<Folder>(mongoDbContext, apiExecutionContext), IFolderRepository
{
    public async Task<Folder?> GetByName(Guid siteId, Guid? parentId, string normalizedName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filter = Builders<Folder>.Filter.Eq(x => x.SiteId, siteId) & Builders<Folder>.Filter.Eq(x => x.ParentId, parentId) & Builders<Folder>.Filter.Eq(x => x.NormalizedName, normalizedName);
        var findResult = await Collection.FindAsync(filter, null, cancellationToken);

        return await findResult.SingleOrDefaultAsync(cancellationToken);
    }
}
