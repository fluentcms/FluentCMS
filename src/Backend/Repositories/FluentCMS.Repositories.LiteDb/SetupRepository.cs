namespace FluentCMS.Repositories.LiteDb;

public class SetupRepository(ILiteDBContext liteDbContext) : ISetupRepository
{
    public async Task<bool> Initialized(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var dbFilePath = GetDatabaseFileName(liteDbContext.ConnectionString);

        // Check if the database file exists
        if (!System.IO.File.Exists(dbFilePath))
            return false; // Database file does not exist

        // Check if the collection exists
        if (!await liteDbContext.Database.CollectionExistsAsync(nameof(GlobalSettings)))
            return false; // Collection does not exist

        return await Task.FromResult(true);
    }

    public Task<bool> InitializeDb(CancellationToken cancellationToken = default)
    {
        // do nothing
        return Task.FromResult(true);
    }

    public static string GetDatabaseFileName(string connectionString)
    {
        // Split the connection string by ';' to get each parameter
        var parameters = connectionString.Split(';');

        // Look for the "Filename" parameter
        foreach (var param in parameters)
        {
            var trimmedParam = param.Trim();
            if (trimmedParam.StartsWith("Filename=", StringComparison.OrdinalIgnoreCase))
            {
                // Return the value after "Filename="
                return trimmedParam["Filename=".Length..];
            }
        }

        // Throw exception if the "Filename" parameter is not found
        throw new ArgumentException("The connection string does not contain a 'Filename' parameter.", nameof(connectionString));
    }

}
