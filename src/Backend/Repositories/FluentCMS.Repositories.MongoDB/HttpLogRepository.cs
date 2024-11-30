﻿using FluentCMS.Entities.Logging;

namespace FluentCMS.Repositories.MongoDB;

public class HttpLogRepository(IMongoDBContext mongoDbContext) : IHttpLogRepository
{
    public async Task Create(HttpLog log, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var collection = mongoDbContext.Database.GetCollection<HttpLog>(GetCollectionName(log.StatusCode));
        var options = new InsertOneOptions { BypassDocumentValidation = false };

        await collection.InsertOneAsync(log, options, cancellationToken);

    }
    private static string GetCollectionName(int statusCode) => statusCode switch
    {
        < 500 and >= 400 => "httphog400",
        < 400 and >= 300 => "httplog300",
        >= 500 => "httplog500",
        _ => "httplog200"
    };
}
