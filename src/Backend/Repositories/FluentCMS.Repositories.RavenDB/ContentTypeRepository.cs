namespace FluentCMS.Repositories.RavenDB;

public class ContentTypeRepository(IRavenDBContext dbContext, IApiExecutionContext apiExecutionContext) : AuditableEntityRepository<ContentType>(dbContext, apiExecutionContext), IContentTypeRepository
{
    public async Task<ContentType?> GetBySlug(string contentTypeSlug, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (var session = Store.OpenAsyncSession())
        {
            return await session.Query<ContentType>().SingleOrDefaultAsync(x => x.Slug == contentTypeSlug);
        }
    }
}
