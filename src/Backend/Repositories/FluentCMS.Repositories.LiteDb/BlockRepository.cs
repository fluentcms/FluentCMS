namespace FluentCMS.Repositories.LiteDb;

public class BlockRepository(ILiteDBContext liteDbContext, IApiExecutionContext apiExecutionContext) : SiteAssociatedRepository<Block>(liteDbContext, apiExecutionContext), IBlockRepository
{
}
