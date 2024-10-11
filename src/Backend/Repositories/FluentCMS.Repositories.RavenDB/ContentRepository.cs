namespace FluentCMS.Repositories.RavenDB;

public class ContentRepository(IRavenDBContext dbContext, IApiExecutionContext apiExecutionContext) : AuditableEntityRepository<Content>(dbContext, apiExecutionContext), IContentRepository
{
    public async Task<IEnumerable<Content>> GetAll(Guid contentTypeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        using (var session = Store.OpenAsyncSession())
        {
            var entities = await session.Query<Content>()
                                    .Where(x => x.TypeId == contentTypeId)
                                    .ToListAsync(cancellationToken);
            
            return entities.AsEnumerable();
        }
    }
}
