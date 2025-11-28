using System.Threading.Tasks;

namespace FluentCMS.Api.Plugins.CmsCoreManagement.Repositories;

// Should be SiteAssociatedRepository
public interface IFileRepository : IRepository<FluentCMS.Api.Core.Models.File>
{
    Task<List<FluentCMS.Api.Core.Models.File>> GetByFolderId(Guid folderId,CancellationToken cancellationToken = default);
}

internal class FileRepository(CmsCoreDbContext dbContext) : Repository<FluentCMS.Api.Core.Models.File, CmsCoreDbContext>(dbContext), IFileRepository
{
    public async Task<List<FluentCMS.Api.Core.Models.File>> GetByFolderId(Guid folderId,CancellationToken cancellationToken)
    {
        return await Query().Where(x => x.FolderId == folderId).ToList(cancellationToken);   
    }
}
