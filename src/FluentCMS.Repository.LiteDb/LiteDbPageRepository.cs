using FluentCMS.Entities.Pages;

namespace FluentCMS.Repository.LiteDb;
internal class LiteDbPageRepository(LiteDbContext dbContext) : LiteDbGenericRepository<Page>(dbContext), IPageRepository
{
    public Task<Page> FindByPath(string path)
    {
        return Collection.FindOneAsync(x => x.Path == path);
    }
}
