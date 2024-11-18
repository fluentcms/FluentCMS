namespace FluentCMS.Repositories.EFCore;

public class BlockRepository(FluentCmsDbContext dbContext, IApiExecutionContext apiExecutionContext) : SiteAssociatedRepository<Block>(dbContext, apiExecutionContext), IBlockRepository
{
}
