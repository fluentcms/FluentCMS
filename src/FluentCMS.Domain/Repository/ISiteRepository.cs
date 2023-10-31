using FluentCMS.Entities.Sites;

namespace FluentCMS.Repository;
public interface ISiteRepository : IGenericRepository<Site>
{
    Task<Site> GetByUrl(string url);
}
