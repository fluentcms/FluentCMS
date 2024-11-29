namespace FluentCMS.Repositories.LiteDb;

public class FileRepository(ILiteDBContext liteDbContext, IApiExecutionContext apiExecutionContext) : SiteAssociatedRepository<File>(liteDbContext, apiExecutionContext), IFileRepository
{
    public async Task<File?> GetByName(Guid siteId, Guid folderId, string normalizedFileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Collection.Query().Where(x => x.SiteId == siteId && x.FolderId == folderId && x.NormalizedName == normalizedFileName).FirstOrDefaultAsync();
    }
}
