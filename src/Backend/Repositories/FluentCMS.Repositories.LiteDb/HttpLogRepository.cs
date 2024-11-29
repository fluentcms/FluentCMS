using FluentCMS.Entities.Logging;

namespace FluentCMS.Repositories.LiteDb;

public class HttpLogRepository: IHttpLogRepository
{
    private readonly ILiteDatabaseAsync _liteDatabase;
    private readonly ILiteDBContext _liteDbContext;

    private static bool _isInitialized = false;

    public HttpLogRepository(ILiteDBContext liteDbContext)
    {
        _liteDatabase = liteDbContext.Database;
        _liteDbContext = liteDbContext;
                
        if (!_isInitialized)
        {
            // check if DB exists and has at least one collection (any collection)
            _isInitialized = _liteDatabase.GetCollectionNamesAsync().GetAwaiter().GetResult().Any();
        }
    }

    public async Task Create(HttpLog log, CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
        {
            cancellationToken.ThrowIfCancellationRequested();

            log.Id = Guid.NewGuid();
            var collection = _liteDbContext.Database.GetCollection<HttpLog>(GetCollectionName(log.StatusCode));

            await collection.InsertAsync(log);
        }
    }

    private static string GetCollectionName(int statusCode) => statusCode switch
    {
        < 500 and >= 400 => "HttpLog400",
        < 400 and >= 300 => "HttpLog300",
        >= 500 => "HttpLog500",
        _ => "HttpLog200"
    };
}
