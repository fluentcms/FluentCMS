namespace FluentCMS.Repositories.Abstractions;

public interface IFolderRepository : ISiteAssociatedRepository<Folder>
{
    Task<Folder?> GetByName(Guid? parentId, string normalizedName, CancellationToken cancellationToken = default);
}
