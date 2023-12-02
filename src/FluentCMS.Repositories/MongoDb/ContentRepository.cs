using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;
using FluentCMS.Repositories.MongoDB;

namespace FluentCMS.Repositories.MongoDb;
public class ContentRepository(IMongoDBContext mongoDbContext, IApplicationContext applicationContext) : GenericRepository<Content>(mongoDbContext, applicationContext), IContentRepository
{
    public override Task<Content?> Create(Content entity, CancellationToken cancellationToken = default)
    {
        return base.Create(entity, cancellationToken);
    }
}
