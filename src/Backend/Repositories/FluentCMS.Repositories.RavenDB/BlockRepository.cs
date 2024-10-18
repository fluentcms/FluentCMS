namespace FluentCMS.Repositories.RavenDB;

public class BlockRepository(IRavenDBContext RavenDbContext, IApiExecutionContext apiExecutionContext) : SiteAssociatedRepository<Block>(RavenDbContext, apiExecutionContext), IBlockRepository
{
}
