namespace FluentCMS.Repositories.EFCore;

public class LayoutRepository(FluentCmsDbContext dbContext, IMapper mapper, IApiExecutionContext apiExecutionContext) : SiteAssociatedRepository<Layout, LayoutModel>(dbContext, mapper, apiExecutionContext), ILayoutRepository;
