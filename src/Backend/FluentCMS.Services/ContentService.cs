namespace FluentCMS.Services;

public interface IContentService : IAutoRegisterService
{
    Task<IEnumerable<Content>> GetAll(Guid contentTypeId, CancellationToken cancellationToken = default);
    Task<Content> Create(Content content, CancellationToken cancellationToken = default);
    Task<Content> Delete(Guid id, CancellationToken cancellationToken = default);
    Task<Content> GetById(Guid id, CancellationToken cancellationToken = default);
    Task<Content> Update(Content content, CancellationToken cancellationToken = default);
}

public class ContentService(IContentRepository contentRepository, IMessagePublisher messagePublisher) : IContentService
{
    public async Task<IEnumerable<Content>> GetAll(Guid contentTypeId, CancellationToken cancellationToken = default)
    {
        return await contentRepository.GetAll(contentTypeId, cancellationToken);
    }

    public async Task<Content> Create(Content content, CancellationToken cancellationToken = default)
    {
        var newContent = await contentRepository.Create(content, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentUnableToCreate);

        await messagePublisher.Publish(new Message<Content>(ActionNames.ContentCreated, newContent), cancellationToken);

        return newContent;
    }

    public async Task<Content> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        var deleted = await contentRepository.Delete(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentUnableToDelete);

        await messagePublisher.Publish(new Message<Content>(ActionNames.ContentDeleted, deleted), cancellationToken);

        return deleted;
    }

    public async Task<Content> Update(Content content, CancellationToken cancellationToken = default)
    {
        var updated = await contentRepository.Update(content, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentUnableToUpdate);

        await messagePublisher.Publish(new Message<Content>(ActionNames.ContentUpdated, updated), cancellationToken);

        return updated;
    }

    public async Task<Content> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        return await contentRepository.GetById(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentNotFound);
    }
}
