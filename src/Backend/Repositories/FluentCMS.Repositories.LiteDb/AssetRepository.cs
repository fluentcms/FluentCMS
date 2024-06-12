namespace FluentCMS.Repositories.LiteDb;

public class AssetRepository : AuditableEntityRepository<Asset>, IAssetRepository
{
    public AssetRepository(ILiteDBContext liteDbContext, IAuthContext authContext) : base(liteDbContext, authContext)
    {
    }
}
