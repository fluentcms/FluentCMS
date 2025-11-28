namespace FluentCMS.Api.Plugins.CmsCoreManagement.Repositories;

// Should be SiteAssociatedRepository
public interface IFolderRepository : IRepository<Folder>
{
    Task<Folder> GetByName(Guid? parentId, string normalizedName, CancellationToken cancellationToken);
    Task<List<Folder>> GetByFolderId(Guid folderId, CancellationToken cancellationToken);
    
}

internal class FolderRepository(CmsCoreDbContext dbContext) : Repository<Folder, CmsCoreDbContext>(dbContext), IFolderRepository
{

    public async Task<Folder> GetByName(Guid? parentId, string normalizedName, CancellationToken cancellationToken)
    {
        return await Query().Where(x => x.ParentId == parentId && x.NormalizedName == normalizedName).SingleOrDefault(cancellationToken);   
    }

    public async Task<List<Folder>> GetByFolderId(Guid folderId, CancellationToken cancellationToken)
    {
        return await Query().Where(x => x.ParentId == folderId).ToList(cancellationToken);   
    }
}
