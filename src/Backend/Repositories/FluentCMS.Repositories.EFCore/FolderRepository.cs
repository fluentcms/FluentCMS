namespace FluentCMS.Repositories.EFCore;

public class FolderRepository(FluentCmsDbContext dbContext, IApiExecutionContext apiExecutionContext) : SiteAssociatedRepository<Folder>(dbContext, apiExecutionContext), IFolderRepository
{
    public async Task<Folder?> GetByName(Guid siteId, Guid? parentId, string normalizedName, CancellationToken cancellationToken = default)
    {
        return await DbContext.Folders.SingleOrDefaultAsync(x => x.SiteId == siteId && x.ParentId == parentId && x.NormalizedName == normalizedName, cancellationToken);
    }
}
