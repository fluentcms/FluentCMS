namespace FluentCMS.Repositories.Caching;

public class FileRepository(IFileRepository repository, ICacheProvider cacheProvider) : SiteAssociatedRepository<Entities.File>(repository, cacheProvider), IFileRepository
{
    public async Task<Entities.File?> GetByName(Guid siteId, Guid folderId, string normalizedFileName, CancellationToken cancellationToken = default)
    {
        var allFiles = await GetAllForSite(siteId, cancellationToken);
        return allFiles.FirstOrDefault(f => f.FolderId == folderId && f.NormalizedName == normalizedFileName);
    }
}
