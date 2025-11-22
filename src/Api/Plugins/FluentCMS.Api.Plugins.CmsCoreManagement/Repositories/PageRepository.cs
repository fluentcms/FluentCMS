namespace FluentCMS.Api.Plugins.CmsCoreManagement.Repositories;

// Should be SiteAssociatedRepository
public interface IPageRepository : IRepository<Page>
{
}

internal class PageRepository(CmsCoreDbContext dbContext) : Repository<Page, CmsCoreDbContext>(dbContext), IPageRepository
{
}
