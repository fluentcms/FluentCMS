namespace FluentCMS.Repositories.LiteDb;

public class BlockRepository(ILiteDBContext liteDbContext, IAuthContext authContext) : SiteAssociatedRepository<Block>(liteDbContext, authContext), IBlockRepository
{
}
