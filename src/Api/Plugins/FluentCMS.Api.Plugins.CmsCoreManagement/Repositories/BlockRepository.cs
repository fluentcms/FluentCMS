using FluentCMS.Api.Core.Repositories.EntityFramework;

namespace FluentCMS.Api.Plugins.CmsCoreManagement.Repositories;

public interface IBlockRepository : ISiteAssociatedRepository<Block>
{
}

internal class BlockRepository(CmsCoreDbContext dbContext) : SiteAssociatedRepository<Block, CmsCoreDbContext>(dbContext), IBlockRepository
{
}
