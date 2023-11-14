using FluentCMS.Entities;

namespace FluentCMS.Repositories.Abstractions;

public interface IAssetRepository : IGenericRepository<Asset>
{
    Task<IEnumerable<Asset>> GetAllOfSite(Guid siteId, CancellationToken cancellationToken = default);
    Task<Asset?> GetAssetByName(Guid siteId, Guid? folderId, string name, CancellationToken cancellationToken = default);
}
