namespace FluentCMS.Repositories.MongoDB;

public class SetupRepository(IMongoDBContext mongoDbContext) : ISetupRepository
{
    public async Task<bool> Initialized(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Check if the collection exists within the database
        var collections = await mongoDbContext.Database.ListCollectionNamesAsync(cancellationToken: cancellationToken);
        var collectionName = nameof(GlobalSettings).ToLowerInvariant();

        // check if collection exists
        var collectionExists = collections.ToList(cancellationToken: cancellationToken).Exists(c => c.Equals(collectionName, StringComparison.InvariantCultureIgnoreCase));

        return collectionExists;
    }

    public Task<bool> InitializeDb(CancellationToken cancellationToken = default)
    {
        // do nothing
        return Task.FromResult(true);
    }
}
