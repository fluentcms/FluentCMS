namespace FluentCMS.Repositories.EFCore;

public class FileRepository(FluentCmsDbContext dbContext, IMapper mapper, IApiExecutionContext apiExecutionContext) : SiteAssociatedRepository<File, FileModel>(dbContext, mapper, apiExecutionContext), IFileRepository
{
    public async Task<File?> GetByName(Guid siteId, Guid folderId, string normalizedFileName, CancellationToken cancellationToken = default)
    {
        var dbEntity = await DbContext.Files.FirstOrDefaultAsync(x => x.SiteId == siteId && x.FolderId == folderId && x.NormalizedName == normalizedFileName, cancellationToken);
        return Mapper.Map<File>(dbEntity);
    }
}
