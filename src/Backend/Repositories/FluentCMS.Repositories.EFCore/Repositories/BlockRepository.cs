namespace FluentCMS.Repositories.EFCore;

public class BlockRepository(FluentCmsDbContext dbContext, IMapper mapper, IApiExecutionContext apiExecutionContext) : SiteAssociatedRepository<Block, BlockModel>(dbContext, mapper, apiExecutionContext), IBlockRepository
{
}
