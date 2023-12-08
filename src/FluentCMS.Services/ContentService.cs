using FluentCMS.Entities;
using FluentCMS.Repositories;

namespace FluentCMS.Services;

public interface IContentService<TContent> where TContent : Content
{
    Task<TContent> Create(TContent content, CancellationToken cancellationToken = default);
    Task Delete(Guid siteId, string contentType, Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<TContent>> GetAll(Guid siteId, string contentType, CancellationToken cancellationToken = default);
    Task<TContent> GetById(Guid siteId, string contentType, Guid id, CancellationToken cancellationToken = default);
    Task<TContent> Update(TContent content, CancellationToken cancellationToken = default);
}

public class ContentService<TContent>(
    IContentRepository<TContent> contentRepository) :
    IContentService<TContent>
    where TContent : Content, new()
{
    public async Task<TContent> Create(TContent content, CancellationToken cancellationToken = default)
    {
        var newContent = await contentRepository.Create(content, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentUnableToCreate);

        return newContent;
    }

    public async Task Delete(Guid siteId, string contentType, Guid id, CancellationToken cancellationToken = default)
    {
        _ = await contentRepository.Delete(siteId, contentType, id, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentUnableToDelete);
    }

    public async Task<IEnumerable<TContent>> GetAll(Guid siteId, string contentType, CancellationToken cancellationToken = default)
    {
        return await contentRepository.GetAll(siteId, contentType, cancellationToken);
    }

    public async Task<TContent> GetById(Guid siteId, string contentType, Guid id, CancellationToken cancellationToken = default)
    {
        return await contentRepository.GetById(siteId, contentType, id, cancellationToken) ??
             throw new AppException(ExceptionCodes.ContentNotFound);
    }

    public async Task<TContent> Update(TContent content, CancellationToken cancellationToken = default)
    {
        var updatedContent = await contentRepository.Update(content, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentUnableToUpdate);

        return updatedContent;
    }
}
