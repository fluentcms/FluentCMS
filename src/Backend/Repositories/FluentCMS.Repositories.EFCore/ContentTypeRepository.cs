namespace FluentCMS.Repositories.EFCore;

public class ContentTypeRepository(FluentCmsDbContext dbContext, IApiExecutionContext apiExecutionContext) : AuditableEntityRepository<ContentType>(dbContext, apiExecutionContext), IContentTypeRepository
{
    public async Task<ContentType?> GetBySlug(string contentTypeSlug, CancellationToken cancellationToken = default)
    {
        return await DbSet.SingleOrDefaultAsync(x => x.Slug == contentTypeSlug, cancellationToken);
    }
}
