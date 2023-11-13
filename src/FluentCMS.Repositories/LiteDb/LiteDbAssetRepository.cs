using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Repositories.LiteDb;

public class LiteDbAssetRepository : LiteDbGenericRepository<Asset>, IAssetRepository
{
    public LiteDbAssetRepository(LiteDbContext dbContext) : base(dbContext)
    {
    }
}
