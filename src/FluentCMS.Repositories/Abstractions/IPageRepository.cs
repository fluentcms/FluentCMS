using FluentCMS.Entities.Pages;

namespace FluentCMS.Repositories.Abstractions;

public interface IPageRepository : IGenericRepository<Page>
{
    Task<IEnumerable<Page>> GetBySiteId(Guid siteId, CancellationToken cancellationToken = default);
    Task<Page> GetByPath(string path);
}
