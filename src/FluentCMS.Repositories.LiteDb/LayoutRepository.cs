using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Repositories.LiteDb;

public class LayoutRepository : SiteAssociatedRepository<Layout>, ILayoutRepository
{
    public LayoutRepository(ILiteDBContext liteDbContext, IAuthContext authContext) : base(liteDbContext, authContext)
    {
    }
}
