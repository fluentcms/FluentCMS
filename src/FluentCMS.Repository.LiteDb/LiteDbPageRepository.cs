using FluentCMS.Entities.Pages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentCMS.Repository.LiteDb;
internal class LiteDbPageRepository : LiteDbGenericRepository<Page>, IPageRepository
{
    public LiteDbPageRepository(LiteDbContext dbContext) : base(dbContext)
    {
    }

    public Task<Page> FindByPath(string path)
    {
        return Collection.FindOneAsync(x => x.Path == path);
    }
}
