namespace FluentCMS.Api.Plugins.CmsCoreManagement.Repositories;

// Should be SiteAssociatedRepository
public interface IPageRepository : IRepository<Page>
{
    Task<Page?> GetByUrl(string url, CancellationToken cancellationToken = default);
}

internal class PageRepository(CmsCoreDbContext dbContext) : Repository<Page, CmsCoreDbContext>(dbContext), IPageRepository
{
}
