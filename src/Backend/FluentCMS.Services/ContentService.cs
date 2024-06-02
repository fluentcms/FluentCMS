namespace FluentCMS.Services;

public interface IContentService : IAutoRegisterService
{
    Task<IEnumerable<Content>> GetAll(Guid contentTypeId, CancellationToken cancellationToken = default);
    Task<Content> Create(Content content, CancellationToken cancellationToken = default);
    Task<Content> Delete(Guid contentTypeId, Guid id, CancellationToken cancellationToken = default);
    Task<Content> Update(Content content, CancellationToken cancellationToken = default);
}

public class ContentService(IContentRepository contentRepository) : IContentService
{
    public async Task<IEnumerable<Content>> GetAll(Guid contentTypeId, CancellationToken cancellationToken = default)
    {
        return await contentRepository.GetAll(contentTypeId, cancellationToken);
    }

    public async Task<Content> Create(Content content, CancellationToken cancellationToken = default)
    {
        var newContent = await contentRepository.Create(content, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentUnableToCreate);

        return newContent;
    }

    public async Task<Content> Delete(Guid contentTypeId, Guid id, CancellationToken cancellationToken = default)
    {
        return await contentRepository.Delete(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentUnableToDelete);
    }

    public async Task<Content> Update(Content content, CancellationToken cancellationToken = default)
    {
        var updatedContent = await contentRepository.Update(content, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentUnableToUpdate);

        return updatedContent;
    }
}
