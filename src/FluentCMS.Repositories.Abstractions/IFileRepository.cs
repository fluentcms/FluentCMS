namespace FluentCMS.Repositories.Abstractions;
public interface IFileRepository: IAuditableEntityRepository<Entities.File>
{
    public Task<Entities.File?> GetBySlug(string slug, CancellationToken cancellationToken);
    public Task<Entities.File?> Delete(string slug, CancellationToken cancellationToken);
}
