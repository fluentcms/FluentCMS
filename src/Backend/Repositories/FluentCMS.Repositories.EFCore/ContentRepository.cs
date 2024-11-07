namespace FluentCMS.Repositories.EFCore;

public class ContentRepository(FluentCmsDbContext dbContext, IApiExecutionContext apiExecutionContext) : AuditableEntityRepository<Content>(dbContext, apiExecutionContext),
    IContentRepository
{
    public async Task<IEnumerable<Content>> GetAll(Guid contentTypeId, CancellationToken cancellationToken = default)
    {
        return await DbSet.Where(x => x.TypeId == contentTypeId).ToListAsync(cancellationToken);
    }
}
