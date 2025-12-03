using FluentCMS.Api.Core.Repositories.EntityFramework;

namespace FluentCMS.Api.Plugins.CmsCoreManagement.Repositories;

// Should be SiteAssociatedRepository
public interface ILayoutRepository : ISiteAssociatedRepository<Layout>
{
}

internal class LayoutRepository(CmsCoreDbContext dbContext) : SiteAssociatedRepository<Layout, CmsCoreDbContext>(dbContext), ILayoutRepository
{
}
