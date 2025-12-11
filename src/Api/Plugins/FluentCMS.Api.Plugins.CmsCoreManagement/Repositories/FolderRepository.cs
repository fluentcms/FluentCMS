using FluentCMS.Api.Core.Repositories.EntityFramework;

namespace FluentCMS.Api.Plugins.CmsCoreManagement.Repositories;

// Should be SiteAssociatedRepository
public interface IFolderRepository : ISiteAssociatedRepository<Folder>
{
    Task<Folder> GetByName(Guid SiteId, Guid? parentId, string normalizedName, CancellationToken cancellationToken);
    Task<List<Folder>> GetByFolderId(Guid folderId, CancellationToken cancellationToken);

}

internal class FolderRepository(CmsCoreDbContext dbContext) : SiteAssociatedRepository<Folder, CmsCoreDbContext>(dbContext), IFolderRepository
{

    public async Task<Folder> GetByName(Guid siteId, Guid? parentId, string normalizedName, CancellationToken cancellationToken)
    {
        var query = Query().Where(x =>x.SiteId == siteId && x.NormalizedName == normalizedName);

        if(parentId != null)
        {
            query = query.Where(x => x.ParentId == parentId!);
        }
        return await query.SingleOrDefault(cancellationToken);
    }

    public async Task<List<Folder>> GetByFolderId(Guid folderId, CancellationToken cancellationToken)
    {
        return await Query().Where(x => x.ParentId == folderId).ToList(cancellationToken);
    }
}
