using FluentCMS.Entities;
using FluentCMS.Entities.Sites;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentCMS.Repository.LiteDb;
public class LiteDbSiteRepository : LiteDbGenericRepository<Site>, ISiteRepository
{
    public LiteDbSiteRepository(LiteDbContext dbContext) : base(dbContext)
    {
    }

    public Task<Site> GetByUrl(string url)
    {
        return Collection.FindOneAsync(x => x.Urls.Contains(url));
    }
}
