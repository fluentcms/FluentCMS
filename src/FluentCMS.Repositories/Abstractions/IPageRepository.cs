using FluentCMS.Entities;

namespace FluentCMS.Repositories;

public interface IPageRepository : ISiteAssociatedRepository<Page>
{
    Task<Page> GetByPath(Guid siteId, string path, CancellationToken cancellationToken = default);
}
