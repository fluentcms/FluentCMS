using FluentCMS.Entities;
using MongoDB.Driver;

namespace FluentCMS.Repositories.MongoDB;

public class HostRepository(
    IMongoDBContext mongoDbContext,
    IApplicationContext applicationContext) :
    IHostRepository
{

    private readonly IMongoCollection<Host> _collection = mongoDbContext.Database.GetCollection<Host>("host");
    private readonly IApplicationContext _applicationContext = applicationContext;

    public async Task<Host?> Create(Host host, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        host.Id = Guid.NewGuid();
        host.CreatedAt = DateTime.UtcNow;
        host.CreatedBy = _applicationContext.Current.Username;
        host.LastUpdatedAt = DateTime.UtcNow;
        host.LastUpdatedBy = _applicationContext.Current.Username;

        await _collection.InsertOneAsync(host, null, cancellationToken);

        return host;
    }

    public async Task<Host?> Get(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await _collection.Find(Builders<Host>.Filter.Empty).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<Host?> Update(Host host, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        host.LastUpdatedAt = DateTime.UtcNow;
        host.LastUpdatedBy = _applicationContext.Current.Username;

        var idFilter = Builders<Host>.Filter.Eq(x => x.Id, host.Id);

        return await _collection.FindOneAndReplaceAsync(idFilter, host, null, cancellationToken);
    }
}
