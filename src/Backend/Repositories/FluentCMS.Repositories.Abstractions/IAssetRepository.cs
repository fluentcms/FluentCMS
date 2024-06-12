namespace FluentCMS.Repositories.Abstractions;

public interface IAssetRepository : IAuditableEntityRepository<Asset>
{
    Task<IEnumerable<Asset>> GetByParentId(Guid? parentId, CancellationToken cancellationToken = default);
}
