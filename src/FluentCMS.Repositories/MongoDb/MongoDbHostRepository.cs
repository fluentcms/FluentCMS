using FluentCMS.Entities;
using MongoDB.Driver;

namespace FluentCMS.Repositories.MongoDb;

public class MongoDbHostRepository : IHostRepository
{
    private readonly IMongoCollection<Host> _collection;

    public MongoDbHostRepository(IMongoDBContext mongoDbContext)
    {
        _collection = mongoDbContext.Database.GetCollection<Host>(nameof(Host));
    }

    public async Task<Host?> Create(Host host, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        host.Id = Guid.NewGuid();
        host.CreatedAt = DateTime.UtcNow;

        await _collection.InsertOneAsync(host, null, cancellationToken);

        return host;
    }

    public Task<Host?> Get(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return _collection.Find(Builders<Host>.Filter.Empty).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<Host?> Update(Host host, CancellationToken cancellationToken = default)
    {

        cancellationToken.ThrowIfCancellationRequested();

        host.LastUpdatedAt = DateTime.UtcNow;

        var idFilter = Builders<Host>.Filter.Eq(x => x.Id, host.Id);

        await _collection.FindOneAndReplaceAsync(idFilter, host, null, cancellationToken);

        return host;

    }
}
