namespace FluentCMS.Api.Core.Repositories.EntityFramework;

public class PageRepository(CmsCoreDbContext dbContext) : SiteAssociatedRepository<Page>(dbContext), IPageRepository
{
}
