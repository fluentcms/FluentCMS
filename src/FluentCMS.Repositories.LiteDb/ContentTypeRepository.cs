using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;
using LiteDB;

namespace FluentCMS.Repositories.LiteDb;

public class ContentTypeRepository (
    ILiteDBContext liteDbContext,
    IAuthContext authContext) :
    AuditableEntityRepository<ContentType>(liteDbContext, authContext),
    IContentTypeRepository
{
    public async Task<ContentType?> GetBySlug(string contentTypeSlug, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filter = Query.EQ(nameof(ContentType.Slug), contentTypeSlug);

        var findResult = await Collection.FindAsync(filter);

        return findResult.SingleOrDefault();
    }
}
