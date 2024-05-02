using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;
using LiteDB;

namespace FluentCMS.Repositories.LiteDb;

public class ContentRepository
(ILiteDBContext liteDbContext,
    IAuthContext authContext) :
    AuditableEntityRepository<Content>(liteDbContext, authContext),
    IContentRepository
{
    public async Task<IEnumerable<Content>> GetAll(Guid contentTypeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var filter = Query.EQ(nameof(Content.TypeId), contentTypeId);
        var findResult = await Collection.FindAsync(filter);
        return findResult.ToList();
    }
}
