
namespace FluentCMS.Repositories.LiteDb;

public class AssetRepository : AuditableEntityRepository<Asset>, IAssetRepository
{
    public AssetRepository(ILiteDBContext liteDbContext, IAuthContext authContext) : base(liteDbContext, authContext)
    {
    }

    public async Task<IEnumerable<Asset>> GetByParentId(Guid? parentId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var findResult = await Collection.Query().Where(x => x.Id == parentId).ToListAsync();
        return findResult.ToList();
    }
}
