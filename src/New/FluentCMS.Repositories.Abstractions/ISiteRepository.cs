namespace FluentCMS.Repositories.Abstractions;

public interface ISiteRepository : IEntityRepository<Site>
{
    Task<Site?> GetByUrl(string url, CancellationToken cancellationToken = default);
}
