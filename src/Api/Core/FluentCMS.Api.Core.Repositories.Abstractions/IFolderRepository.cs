namespace FluentCMS.Api.Core.Repositories.Abstractions;

public interface IFolderRepository : ISiteAssociatedRepository<Folder>
{
    Task<Folder?> GetByName(Guid siteId, Guid? parentId, string normalizedName, CancellationToken cancellationToken = default);
}
