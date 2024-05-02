using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;
using LiteDB;

namespace FluentCMS.Repositories.LiteDb;

public class SiteRepository : AuditableEntityRepository<Site>, ISiteRepository
{
    public SiteRepository(ILiteDBContext liteDbContext, IAuthContext authContext) : base(liteDbContext, authContext)
    {
    }
    public async Task<Site?> GetByUrl(string url, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filter = Query.Any().EQ(nameof(Site.Urls),url);
        var findResult = await Collection.FindAsync(filter);

        return findResult.SingleOrDefault();
    }
}
