namespace FluentCMS.Repositories.LiteDb;

public class BlockRepository : AuditableEntityRepository<Block>, IBlockRepository
{
    public BlockRepository(ILiteDBContext liteDbContext, IAuthContext authContext) : base(liteDbContext, authContext)
    {
    }
}
