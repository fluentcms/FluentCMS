using FluentCMS.Entities.Logging;

namespace FluentCMS.Repositories.LiteDb;

public class HttpLogRepository(ILiteDBContext liteDbContext) : IHttpLogRepository
{
    protected readonly ILiteDatabaseAsync LiteDatabase = liteDbContext.Database;
    protected readonly ILiteDBContext LiteDbContext = liteDbContext;

    public async Task Create(HttpLog log, CancellationToken cancellationToken = default)
    {
        log.Id = Guid.NewGuid();

        cancellationToken.ThrowIfCancellationRequested();

        var collectionName = $"{nameof(HttpLog)}s{log.StatusCode.ToString()[..1]}";
        var collection = LiteDbContext.Database.GetCollection < HttpLog>(collectionName);
        
        await collection.InsertAsync(log);
    }
}
