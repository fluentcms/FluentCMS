using FluentCMS.Entities;
using MongoDB.Driver;

namespace FluentCMS.Repositories.MongoDB;

public class HostRepository : IHostRepository
{
    private readonly IMongoCollection<Host> _collection;
    private readonly IApplicationContext _applicationContext;

    public HostRepository(IMongoDBContext mongoDbContext, IApplicationContext applicationContext)
    {
        _collection = mongoDbContext.Database.GetCollection<Host>(nameof(Host));
        _applicationContext = applicationContext;
    }

    public async Task<Host?> Create(Host host, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        host.Id = Guid.NewGuid();
        host.CreatedAt = DateTime.UtcNow;
        host.CreatedBy = _applicationContext.Current.UserName;
        host.LastUpdatedAt = DateTime.UtcNow;
        host.LastUpdatedBy = _applicationContext.Current.UserName;

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
        host.LastUpdatedBy = _applicationContext.Current.UserName;

        var idFilter = Builders<Host>.Filter.Eq(x => x.Id, host.Id);

        await _collection.FindOneAndReplaceAsync(idFilter, host, null, cancellationToken);

        return host;

    }
}
