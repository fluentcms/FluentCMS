
namespace FluentCMS.Repositories.MongoDB;

public class AssetRepository : AuditableEntityRepository<Asset>, IAssetRepository
{
    public AssetRepository(IMongoDBContext mongoDbContext, IAuthContext authContext) : base(mongoDbContext, authContext)
    {
    }

    public async Task<IEnumerable<Asset>> GetByParentId(Guid? parentId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filter = Builders<Asset>.Filter.Eq(x => x.FolderId, parentId);
        var findResult = await Collection.FindAsync(filter, null, cancellationToken);
        return await findResult.ToListAsync(cancellationToken);
    }
}
