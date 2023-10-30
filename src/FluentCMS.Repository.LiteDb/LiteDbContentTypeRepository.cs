using FluentCMS.Entities.ContentTypes;

namespace FluentCMS.Repository.LiteDb;

internal class LiteDbContentTypeRepository : LiteDbGenericRepository<ContentType>, IContentTypeRepository
{
    public LiteDbContentTypeRepository(LiteDbContext dbContext) : base(dbContext)
    {
    }

    public async Task<ContentType?> GetBySlug(string slug)
    {
        var data = await this.Collection.FindOneAsync(X => X.Slug == slug);
        return data;
    }
}
