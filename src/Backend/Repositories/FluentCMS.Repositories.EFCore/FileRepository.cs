namespace FluentCMS.Repositories.EFCore;

public class FileRepository(FluentCmsDbContext dbContext, IApiExecutionContext apiExecutionContext) : SiteAssociatedRepository<File>(dbContext, apiExecutionContext), IFileRepository
{
    public async Task<File?> GetByName(Guid siteId, Guid folderId, string normalizedFileName, CancellationToken cancellationToken = default)
    {
        return await DbSet.FirstOrDefaultAsync(x => x.SiteId == siteId && x.FolderId == folderId && x.NormalizedName == normalizedFileName, cancellationToken);
    }
}
