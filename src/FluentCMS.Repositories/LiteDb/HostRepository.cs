//using FluentCMS.Entities;
//using LiteDB.Async;

//namespace FluentCMS.Repositories.LiteDb;

//public class HostRepository : IHostRepository
//{
//    private readonly LiteDbContext _dbContext;
//    private readonly IApplicationContext _applicationContext;

//    public HostRepository(LiteDbContext dbContext, IApplicationContext applicationContext)
//    {
//        _dbContext = dbContext;
//        _applicationContext = applicationContext;
//    }

//    private ILiteCollectionAsync<Host> Collection => _dbContext.Database.GetCollection<Host>(typeof(Host).Name);

//    public async Task<Host?> Create(Host host, CancellationToken cancellationToken = default)
//    {
//        cancellationToken.ThrowIfCancellationRequested();

//        host.Id = Guid.NewGuid();
//        host.CreatedAt = DateTime.UtcNow;
//        host.CreatedBy = _applicationContext.Current.UserName;
//        host.LastUpdatedAt = DateTime.UtcNow;
//        host.LastUpdatedBy = _applicationContext.Current.UserName;

//        await Collection.InsertAsync(host);
//        return host;
//    }

//    public async Task<Host?> Get(CancellationToken cancellationToken = default)
//    {
//        cancellationToken.ThrowIfCancellationRequested();

//        var model = await Collection.FindOneAsync(x => true);
//        return model;
//    }

//    public async Task<Host?> Update(Host host, CancellationToken cancellationToken = default)
//    {
//        cancellationToken.ThrowIfCancellationRequested();

//        host.LastUpdatedAt = DateTime.UtcNow;
//        host.LastUpdatedBy = _applicationContext.Current.UserName;

//        return await Collection.UpdateAsync(host) ? host : default;
//    }
//}
