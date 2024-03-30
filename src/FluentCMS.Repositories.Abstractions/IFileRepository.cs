namespace FluentCMS.Repositories.Abstractions;
public interface IFileRepository
{
    public Task<IEnumerable<Entities.File>> GetFiles(CancellationToken cancellationToken);
    public Task<Entities.File> GetFile(Guid id, CancellationToken cancellationToken);
    public Task<Entities.File> GetFile(string slug, CancellationToken cancellationToken);
    public Task DeleteFile(Guid id, CancellationToken cancellationToken);
    public Task DeleteFile(string slug, CancellationToken cancellationToken);
    public Task Create(Entities.File file, CancellationToken cancellationToken);
}
