namespace FluentCMS.Api.Plugins.CmsCoreManagement.Repositories;

// Should be SiteAssociatedRepository
public interface ILayoutRepository : IRepository<Layout>
{
}

internal class LayoutRepository(CmsCoreDbContext dbContext) : Repository<Layout, CmsCoreDbContext>(dbContext), ILayoutRepository
{
}
