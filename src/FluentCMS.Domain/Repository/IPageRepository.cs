using FluentCMS.Entities.Pages;

namespace FluentCMS.Repository;
public interface IPageRepository : IGenericRepository<Page>
{
    public Task<Page> FindByPath(string path);
}
