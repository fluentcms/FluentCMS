namespace FluentCMS.Repositories.EFCore;

public class PageRepository(FluentCmsDbContext dbContext, IApiExecutionContext apiExecutionContext) : SiteAssociatedRepository<Page>(dbContext, apiExecutionContext), IPageRepository
{
}
