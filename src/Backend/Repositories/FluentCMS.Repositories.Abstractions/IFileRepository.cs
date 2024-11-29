namespace FluentCMS.Repositories.Abstractions;

public interface IFileRepository : ISiteAssociatedRepository<File>
{
    Task<File?> GetByName(Guid siteId, Guid folderId, string normalizedFileName, CancellationToken cancellationToken = default);
}
