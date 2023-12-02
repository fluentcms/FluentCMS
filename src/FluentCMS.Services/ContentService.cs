using FluentCMS.Entities;
using FluentCMS.Repositories.Abstractions;

namespace FluentCMS.Services;

public interface IContentService
{
    Task<Content> Create(Content content, CancellationToken cancellationToken = default);
    Task Delete(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<Content>> GetAll(CancellationToken cancellationToken = default);
    Task<Content> GetById(Guid id, CancellationToken cancellationToken = default);
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

    public async Task Delete(Guid id, CancellationToken cancellationToken = default)
    {
        _ = await contentRepository.Delete(id, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentUnableToDelete);
    }

    public async Task<IEnumerable<Content>> GetAll(CancellationToken cancellationToken = default)
    {
        return await contentRepository.GetAll(cancellationToken);
    }

    public async Task<Content> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        return await contentRepository.GetById(id, cancellationToken) ??
             throw new AppException(ExceptionCodes.ContentNotFound);
    }

    public async Task<Content> Update(Content content, CancellationToken cancellationToken = default)
    {
        var updatedContent = await contentRepository.Update(content, cancellationToken) ??
            throw new AppException(ExceptionCodes.ContentUnableToUpdate);

        return updatedContent;
    }
}
