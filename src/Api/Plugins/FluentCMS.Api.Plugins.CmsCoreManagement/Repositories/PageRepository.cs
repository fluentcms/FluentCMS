using FluentCMS.Api.Core.Repositories.EntityFramework;

namespace FluentCMS.Api.Plugins.CmsCoreManagement.Repositories;

public interface IPageRepository : ISiteAssociatedRepository<Page>
{
}

internal class PageRepository(CmsCoreDbContext dbContext) : SiteAssociatedRepository<Page, CmsCoreDbContext>(dbContext), IPageRepository
{
}
