using FluentCMS.Entities.Logging;

namespace FluentCMS.Repositories.LiteDb;

public class HttpLogRepository(ILiteDBContext liteDbContext) : IHttpLogRepository
{
    protected readonly ILiteDatabaseAsync LiteDatabase = liteDbContext.Database;
    protected readonly ILiteDBContext LiteDbContext = liteDbContext;

    public async Task Create(HttpLog log, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        log.Id = Guid.NewGuid();
        var collection = LiteDbContext.Database.GetCollection<HttpLog>(GetCollectionName(log.StatusCode));

        await collection.InsertAsync(log);
    }

    private static string GetCollectionName(int statusCode) => statusCode switch
    {
        < 500 and >= 400 => "HttpLog400",
        < 400 and >= 300 => "HttpLog300",
        >= 500 => "HttpLog500",
        _ => "HttpLog200"
    };
}
