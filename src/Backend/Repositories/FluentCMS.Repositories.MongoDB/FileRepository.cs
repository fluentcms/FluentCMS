namespace FluentCMS.Repositories.MongoDB;

public class FileRepository(IMongoDBContext mongoDbContext, IApiExecutionContext apiExecutionContext) : SiteAssociatedRepository<File>(mongoDbContext, apiExecutionContext), IFileRepository
{
    public async Task<File?> GetByName(Guid siteId, Guid folderId, string normalizedFileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Collection.Find(x => x.SiteId == siteId && x.FolderId == folderId && x.NormalizedName == normalizedFileName).FirstOrDefaultAsync(cancellationToken);
    }
}

