using FluentCMS.Entities;
using MongoDB.Driver;

namespace FluentCMS.Repositories.MongoDB;

public class ContentTypeRepository(
    IMongoDBContext mongoDbContext,
    IApplicationContext applicationContext) :
    AppAssociatedRepository<ContentType>(mongoDbContext, applicationContext),
    IContentTypeRepository
{
    public async Task<ContentType?> GetBySlug(Guid appId, string contentTypeSlug, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var filter = Builders<ContentType>.Filter.Eq(x => x.Slug, contentTypeSlug);

        var findResult = await Collection.FindAsync(filter, null, cancellationToken);

        return findResult.SingleOrDefault(cancellationToken);
    }

    public async Task<ContentType?> AddField(Guid contentTypeId, ContentTypeField field, CancellationToken cancellationToken = default)
    {
        var contentType = await GetById(contentTypeId, cancellationToken);

        if (contentType == null)
            return null;

        contentType.Fields.Add(field);

        return await Update(contentType, cancellationToken);
    }

    public async Task<ContentType?> RemoveField(Guid contentTypeId, string fieldSlug, CancellationToken cancellationToken = default)
    {
        var contentType = await GetById(contentTypeId, cancellationToken);

        if (contentType == null)
            return null;

        var field = contentType.Fields.Single(x => x.Slug == fieldSlug);

        if (field == null)
            return null;

        contentType.Fields.Remove(field);

        return await Update(contentType, cancellationToken);
    }

    public async Task<ContentType?> UpdateField(Guid contentTypeId, ContentTypeField field, CancellationToken cancellationToken = default)
    {
        var contentType = await GetById(contentTypeId, cancellationToken);

        if (contentType == null)
            return null;

        var original = contentType.Fields.Single(x => x.Slug == field.Slug);

        if (original == null)
            return null;

        contentType.Fields.Remove(original);

        contentType.Fields.Add(field);

        return await Update(contentType, cancellationToken);
    }

}
