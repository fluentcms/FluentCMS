
namespace FluentCMS.Repositories.LiteDb;

public class FolderRepository(ILiteDBContext liteDbContext, IApiExecutionContext apiExecutionContext) : SiteAssociatedRepository<Folder>(liteDbContext, apiExecutionContext), IFolderRepository
{
    public async Task<Folder?> GetByName(Guid? parentId, string normalizedName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Collection.Query().Where(x => x.ParentId == parentId && x.NormalizedName == normalizedName).SingleOrDefaultAsync();
    }
}
