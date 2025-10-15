namespace FluentCMS.Api.Core.Repositories.EntityFramework;

public class LayoutRepository(CmsCoreDbContext dbContext) : SiteAssociatedRepository<Layout>(dbContext), ILayoutRepository;
