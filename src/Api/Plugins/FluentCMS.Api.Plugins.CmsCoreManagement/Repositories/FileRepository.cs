using FluentCMS.Api.Core.Repositories.EntityFramework;
using System.Threading.Tasks;

namespace FluentCMS.Api.Plugins.CmsCoreManagement.Repositories;

// Should be SiteAssociatedRepository
public interface IFileRepository : ISiteAssociatedRepository<Core.Models.File>
{
    Task<List<Core.Models.File>> GetByFolderId(Guid folderId, CancellationToken cancellationToken = default);
    Task<Core.Models.File> GetByName(Guid SiteId, Guid? folderId, string normalizedName, CancellationToken cancellationToken);
}

internal class FileRepository(CmsCoreDbContext dbContext) : SiteAssociatedRepository<Core.Models.File, CmsCoreDbContext>(dbContext), IFileRepository
{
    public async Task<List<Core.Models.File>> GetByFolderId(Guid folderId, CancellationToken cancellationToken)
    {
        return await Query().Where(x => x.FolderId == folderId).ToList(cancellationToken);
    }
    public async Task<Core.Models.File> GetByName(Guid SiteId, Guid? folderId, string normalizedName, CancellationToken cancellationToken)
    {
        return await Query().Where(x => x.FolderId == folderId && x.NormalizedName == normalizedName).SingleOrDefault(cancellationToken);
    }

}
