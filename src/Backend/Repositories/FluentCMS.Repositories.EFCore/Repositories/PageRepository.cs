namespace FluentCMS.Repositories.EFCore;

public class PageRepository(FluentCmsDbContext dbContext, IMapper mapper, IApiExecutionContext apiExecutionContext) : SiteAssociatedRepository<Page, PageModel>(dbContext, mapper, apiExecutionContext), IPageRepository
{
}
