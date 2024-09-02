namespace FluentCMS.Repositories.MongoDB;

public class BlockRepository(IMongoDBContext mongoDbContext, IAuthContext authContext) : SiteAssociatedRepository<Block>(mongoDbContext, authContext), IBlockRepository
{
}
