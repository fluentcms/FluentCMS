using FluentCMS.Entities;
using FluentCMS.Repositories;
using LiteDB.Async;

namespace FluentCMS.Repositories.LiteDb;

public class LiteDbHostRepository : IHostRepository
{
    private readonly LiteDbContext _dbContext;
    public LiteDbHostRepository(LiteDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    private ILiteCollectionAsync<Host> Collection => _dbContext.Database.GetCollection<Host>(typeof(Host).Name);

    public async Task<Host?> Create(Host host, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        host.Id = Guid.NewGuid();
        host.CreatedAt = DateTime.UtcNow;
        host.LastUpdatedAt = DateTime.UtcNow;

        await Collection.InsertAsync(host);
        return host;
    }

    public async Task<Host?> Get(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var model = await Collection.FindOneAsync(x => true);
        return model;
    }

    public async Task<Host?> Update(Host host, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        host.LastUpdatedAt = DateTime.UtcNow;
        return await Collection.UpdateAsync(host) ? host : default;
    }
}
