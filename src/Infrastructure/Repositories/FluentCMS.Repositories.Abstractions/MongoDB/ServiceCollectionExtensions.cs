using FluentCMS.Repositories.Abstractions;
using MongoDB.Driver;

namespace FluentCMS.Repositories.MongoDB;

// Extension methods for configuring MongoDB database connections
public static class MongoServiceCollectionExtensions
{
    // Add UseMongoDB method to the connection builder
    public static void UseMongoDB(this IDatabaseConnectionBuilder builder, string connectionString, string databaseName)
    {
        // This extension is available when the MongoDB package is referenced
        // It provides the implementation for UseMongoDB()
        builder.SetFactory(() =>
        {
            var mongoClient = new MongoClient(connectionString);
            var database = mongoClient.GetDatabase(databaseName);
            return new MongoDataContext(database);
        });
    }
}
