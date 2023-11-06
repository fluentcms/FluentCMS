using FluentCMS.Entities.Pages;

namespace FluentCMS.Repositories.Abstractions;

public interface IPageRepository : IGenericRepository<Page>
{
    Task<IEnumerable<Page>> GetBySiteId(Guid siteId, CancellationToken cancellationToken = default);
    Task<Page> GetByPath(string path);
    Task<IEnumerable<Page>> GetByParentId(Guid id);
    Task<IEnumerable<Page>> GetBySiteIdAndParentId(Guid siteId, Guid? parentId=null);
}
