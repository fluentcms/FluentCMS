namespace FluentCMS.Repositories.EFCore;

public class LayoutRepository(FluentCmsDbContext dbContext, IApiExecutionContext apiExecutionContext) : SiteAssociatedRepository<Layout>(dbContext, apiExecutionContext), ILayoutRepository
{
}
