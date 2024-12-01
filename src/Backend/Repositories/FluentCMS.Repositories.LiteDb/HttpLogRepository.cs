namespace FluentCMS.Repositories.LiteDb;

public class HttpLogRepository(ILiteDBContext liteDbContext) : IHttpLogRepository
{
    public async Task CreateMany(IEnumerable<HttpLog> httpLogs, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (httpLogs == null)
            return;

        // Create a list of tasks to insert the HTTP logs into the appropriate collection.
        var tasks = httpLogs.GroupBy(log => GetCollectionName(log.StatusCode))
            .Select(async group =>
            {
                // Get the collection name from the group.
                var collectionName = group.Key;
                // Get the collection from the database.
                var collection = liteDbContext.Database.GetCollection<HttpLog>(collectionName);
                // Insert the HTTP logs into the collection.
                await collection.InsertBulkAsync(group);
            });

        // Wait for all tasks to complete.
        await Task.WhenAll(tasks);
    }

    private static string GetCollectionName(int statusCode) => statusCode switch
    {
        < 500 and >= 400 => "httphog400",
        < 400 and >= 300 => "httplog300",
        >= 500 => "httplog500",
        _ => "httplog200"
    };
}
