namespace FluentCMS.Repositories.MongoDB;

public class BlockRepository : AuditableEntityRepository<Block>, IBlockRepository
{
    public BlockRepository(IMongoDBContext mongoDbContext, IAuthContext authContext) : base(mongoDbContext, authContext)
    {
    }
}