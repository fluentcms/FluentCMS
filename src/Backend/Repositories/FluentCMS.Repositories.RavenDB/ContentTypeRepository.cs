namespace FluentCMS.Repositories.RavenDB;

public class ContentTypeRepository(IRavenDBContext dbContext, IApiExecutionContext apiExecutionContext) : AuditableEntityRepository<ContentType>(dbContext, apiExecutionContext), IContentTypeRepository
{
    public async Task<ContentType?> GetBySlug(string contentTypeSlug, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using (var session = Store.OpenAsyncSession())
        {
            var entity = await session.Query<RavenEntity<ContentType>>().SingleOrDefaultAsync(x => x.Data.Slug == contentTypeSlug, cancellationToken);

            return entity?.Data;
        }
    }
}
