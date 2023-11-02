using Ardalis.GuardClauses;
using FluentCMS.Entities.ContentTypes;
using FluentCMS.Repository;

namespace FluentCMS.Application.Services;

public interface IContentTypeService
{
    Task<ContentType> GetById(Guid id);
    Task<ContentType?> GetBySlug(string slug);
    Task<IEnumerable<ContentType>> Search();
    Task Create(ContentType contentType);
    Task Edit(ContentType contentType);
    Task Delete(Guid id);
}

internal class ContentTypeService : IContentTypeService
{
    private readonly IContentTypeRepository _contentTypeRepository;

    public ContentTypeService(IContentTypeRepository contentTypeRepository)
    {
        _contentTypeRepository = contentTypeRepository;
    }

    public async Task<ContentType> GetById(Guid id)
    {
        return await _contentTypeRepository.GetById(id)
            ?? throw new ApplicationException("Requested contentType does not exists.");
    }

    public async Task<ContentType?> GetBySlug(string slug)
    {
        return await _contentTypeRepository.GetBySlug(slug);
    }

    public async Task<IEnumerable<ContentType>> Search()
    {
        return await _contentTypeRepository.GetAll();
    }

    public async Task Create(ContentType contentType)
    {
        Guard.Against.Null(contentType);
        Guard.Against.NullOrEmpty(contentType.Title);
        Guard.Against.NullOrEmpty(contentType.Slug);

        await CheckForDuplicateSlug(contentType);
        await _contentTypeRepository.Create(contentType);
    }

    public async Task Edit(ContentType contentType)
    {
        Guard.Against.Null(contentType);
        Guard.Against.NullOrEmpty(contentType.Title);
        Guard.Against.NullOrEmpty(contentType.Slug);

        await CheckForDuplicateSlug(contentType);
        await _contentTypeRepository.Update(contentType);
    }

    public async Task Delete(Guid id)
    {
        Guard.Against.NullOrEmpty(id);
        await _contentTypeRepository.Delete(id);
    }

    private async Task CheckForDuplicateSlug(ContentType contentType)
    {
        // todo: add this query to repository?
        if ((await _contentTypeRepository.GetAll(x => x.Slug == contentType.Slug && x.Id != contentType.Id)).Any())
        {
            throw new ApplicationException("A content type with the same slug already exists.");
        }
    }
}
