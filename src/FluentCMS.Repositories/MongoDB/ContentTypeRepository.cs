using FluentCMS.Entities;
using MongoDB.Driver;

namespace FluentCMS.Repositories.MongoDB;

public class ContentTypeRepository(
    IMongoDBContext mongoDbContext,
    IApplicationContext applicationContext) :
    AuditEntityRepository<ContentType>(mongoDbContext, applicationContext),
    IContentTypeRepository
{
    public async Task<ContentType?> GetByName(string name, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filter = Builders<ContentType>.Filter.Eq(x => x.Name, name);

        var findResult = await Collection.FindAsync(filter, null, cancellationToken);

        return findResult.SingleOrDefault(cancellationToken);
    }

    public async Task<ContentType?> AddField(Guid contentTypeId, ContentTypeField field, CancellationToken cancellationToken = default)
    {
        var contentType = await GetByContentTypeId(contentTypeId, cancellationToken);
        contentType.Fields.Add(field);
        return await Update(contentType, cancellationToken);
    }

    public async Task<ContentType?> RemoveField(Guid contentTypeId, string fieldName, CancellationToken cancellationToken = default)
    {
        var contentType = await GetByContentTypeId(contentTypeId, cancellationToken);
        var field = contentType.Fields.Single(x => x.Name == fieldName);
        contentType.Fields.Remove(field);
        return await Update(contentType, cancellationToken);
    }

    public async Task<ContentType?> UpdateField(Guid contentTypeId, ContentTypeField field, CancellationToken cancellationToken = default)
    {
        var contentType = await GetByContentTypeId(contentTypeId, cancellationToken);
        var original = contentType.Fields.Single(x => x.Name == field.Name);
        contentType.Fields.Remove(original);
        contentType.Fields.Add(field);
        return await Update(contentType, cancellationToken);
    }

    private async Task<ContentType> GetByContentTypeId(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filter = Builders<ContentType>.Filter.Eq(x => x.Id, id);

        var findResult = await Collection.FindAsync(filter, null, cancellationToken);

        return findResult.Single(cancellationToken);
    }
}
