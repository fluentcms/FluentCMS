namespace FluentCMS.Repositories.LiteDb;

public class ContentRepository(
    ILiteDBContext liteDbContext,
    IAuthContext authContext) :
    AuditableEntityRepository<Content>(liteDbContext, authContext),
    IContentRepository
{
    public async Task<IEnumerable<Content>> GetAll(Guid contentTypeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Collection.Query().Where(x => x.TypeId == contentTypeId).ToListAsync();
    }
}
