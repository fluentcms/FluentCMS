namespace FluentCMS.Repositories.Abstractions;

public interface ISiteRepository : IAuditableEntityRepository<Site>
{
    Task<Site?> GetByUrl(string url, CancellationToken cancellationToken = default);
}
