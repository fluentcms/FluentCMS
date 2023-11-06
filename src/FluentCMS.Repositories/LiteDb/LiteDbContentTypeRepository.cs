using FluentCMS.Entities.ContentTypes;
using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Repositories.LiteDb;

public class LiteDbContentTypeRepository : LiteDbGenericRepository<ContentType>, IContentTypeRepository
{
    public LiteDbContentTypeRepository(LiteDbContext dbContext) : base(dbContext)
    {
    }

    public async Task<ContentType?> GetBySlug(string slug, CancellationToken cancellationToken = default)
    {
        var data = await Collection.FindOneAsync(x => x.Slug == slug);
        return data;
    }

    public async Task<bool> SlugExists(string slug, Guid exceptId, CancellationToken cancellationToken = default)
    {
        return (await this.GetAll(x => x.Slug == slug && x.Id != exceptId)).Any();
    }
}
