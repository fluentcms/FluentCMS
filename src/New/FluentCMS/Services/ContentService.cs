using FluentCMS.Entities;
using FluentCMS.Repositories;

namespace FluentCMS.Services;

public interface IContentService<TContent> where TContent : Content
{
    Task<TContent> Create(TContent content, CancellationToken cancellationToken = default);
    Task Delete(string contentType, Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<TContent>> GetAll(string contentType, Guid siteId, CancellationToken cancellationToken = default);
    Task<TContent> GetById(string contentType, Guid id, CancellationToken cancellationToken = default);
    Task<TContent> Update(TContent content, CancellationToken cancellationToken = default);
}

public class ContentService<TContent>(
    IContentRepository<TContent> contentRepository) :
    IContentService<TContent>
    where TContent : Content, new()
{
    public virtual async Task<TContent> Create(TContent content, CancellationToken cancellationToken = default)
    {
        var newContent = await contentRepository.Create(content, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentUnableToCreate);

        return newContent;
    }

    public virtual async Task Delete(string contentType, Guid id, CancellationToken cancellationToken = default)
    {
        _ = await contentRepository.Delete(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentUnableToDelete);
    }

    public virtual async Task<IEnumerable<TContent>> GetAll(string contentType, Guid siteId, CancellationToken cancellationToken = default)
    {
        return await contentRepository.GetAllForSite(contentType, siteId, cancellationToken);
    }

    public virtual async Task<TContent> GetById(string contentType, Guid id, CancellationToken cancellationToken = default)
    {
        return await contentRepository.GetById(id, cancellationToken) ??
             throw new AppException(ExceptionCodes.ContentNotFound);
    }

    public virtual async Task<TContent> Update(TContent content, CancellationToken cancellationToken = default)
    {
        var updatedContent = await contentRepository.Update(content, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentUnableToUpdate);

        return updatedContent;
    }
}
