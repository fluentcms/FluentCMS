namespace FluentCMS.Repositories.LiteDb;

public class ContentRepository(ILiteDBContext liteDbContext, IApiExecutionContext apiExecutionContext) : AuditableEntityRepository<Content>(liteDbContext, apiExecutionContext),
    IContentRepository
{
    public async Task<IEnumerable<Content>> GetAll(Guid contentTypeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Collection.Query().Where(x => x.TypeId == contentTypeId).ToListAsync();
    }
}
