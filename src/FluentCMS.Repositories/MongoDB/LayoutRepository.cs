using FluentCMS.Entities;

namespace FluentCMS.Repositories.MongoDB;

public class LayoutRepository(
    IMongoDBContext mongoDbContext,
    IApplicationContext applicationContext) :
    SiteAssociatedRepository<Layout>(mongoDbContext, applicationContext),
    ILayoutRepository
{
}
