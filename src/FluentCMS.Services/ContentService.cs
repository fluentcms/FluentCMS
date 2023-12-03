using FluentCMS.Entities;
using FluentCMS.Repositories;

namespace FluentCMS.Services;

public interface IContentService
{
    Task<Content> Create(Content content, CancellationToken cancellationToken = default);
    Task Delete(string contentType, Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<Content>> GetAll(string contentType, CancellationToken cancellationToken = default);
    Task<Content> GetById(string contentType, Guid id, CancellationToken cancellationToken = default);
    Task<Content> Update(Content content, CancellationToken cancellationToken = default);
}

public class ContentService(IContentRepository contentRepository) : IContentService
{
    public async Task<Content> Create(Content content, CancellationToken cancellationToken = default)
    {
        var newContent = await contentRepository.Create(content, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentUnableToCreate);

        return newContent;
    }

    public async Task Delete(string contentType, Guid id, CancellationToken cancellationToken = default)
    {
        _ = await contentRepository.Delete(contentType, id, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentUnableToDelete);
    }

    public async Task<IEnumerable<Content>> GetAll(string contentType, CancellationToken cancellationToken = default)
    {
        return await contentRepository.GetAll(contentType, cancellationToken);
    }

    public async Task<Content> GetById(string contentType, Guid id, CancellationToken cancellationToken = default)
    {
        return await contentRepository.GetById(contentType, id, cancellationToken) ??
             throw new AppException(ExceptionCodes.ContentNotFound);
    }

    public async Task<Content> Update(Content content, CancellationToken cancellationToken = default)
    {
        var updatedContent = await contentRepository.Update(content, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentUnableToUpdate);

        return updatedContent;
    }
}
