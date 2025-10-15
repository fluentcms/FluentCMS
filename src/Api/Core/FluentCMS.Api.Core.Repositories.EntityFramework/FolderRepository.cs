namespace FluentCMS.Api.Core.Repositories.EntityFramework;

public class FolderRepository(CmsCoreDbContext dbContext) : SiteAssociatedRepository<Folder>(dbContext), IFolderRepository
{
    public async Task<Folder?> GetByName(Guid siteId, Guid? parentId, string normalizedName, CancellationToken cancellationToken = default)
    {
        return await Query().FirstOrDefault(x => x.SiteId == siteId && x.ParentId == parentId && x.NormalizedName == normalizedName, cancellationToken);
    }
}
