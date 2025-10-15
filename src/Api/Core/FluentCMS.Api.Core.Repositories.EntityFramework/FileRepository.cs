using File = FluentCMS.Api.Core.Models.File;

namespace FluentCMS.Api.Core.Repositories.EntityFramework;

public class FileRepository(CmsCoreDbContext dbContext) : SiteAssociatedRepository<File>(dbContext), IFileRepository
{
    public async Task<File?> GetByName(Guid siteId, Guid folderId, string normalizedFileName, CancellationToken cancellationToken = default)
    {
        return await Query().FirstOrDefault(x => x.SiteId == siteId && x.FolderId == folderId && x.NormalizedName == normalizedFileName, cancellationToken);
    }
}
