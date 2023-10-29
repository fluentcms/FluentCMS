using Ardalis.GuardClauses;
using FluentCMS.Entities.ContentTypes;
using FluentCMS.Repository;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FluentCMS.Services;
public class ContentTypeService
{
    private readonly IContentTypeRepository _contentTypeRepository;

    public ContentTypeService(IContentTypeRepository contentTypeRepository)
    {
        _contentTypeRepository = contentTypeRepository;
    }

    public async Task Create(ContentType contentType)
    {
        Guard.Against.Null(contentType);
        Guard.Against.NullOrEmpty(contentType.Title);
        Guard.Against.NullOrEmpty(contentType.Slug);
        await CheckForDuplicateSlug(contentType);
        await _contentTypeRepository.Create(contentType);
    }

    public async Task Edit(ContentType contentType) {
        Guard.Against.Null(contentType);
        Guard.Against.NullOrEmpty(contentType.Title);
        Guard.Against.NullOrEmpty(contentType.Slug);
        await CheckForDuplicateSlug(contentType);
        await _contentTypeRepository.Update(contentType); 
    }

    public async Task Delete(ContentType contentType)
    {
        Guard.Against.Null(contentType);
        await _contentTypeRepository.Delete(contentType.Id);
    }

    public async Task<IEnumerable<ContentType>> GetAll()
    {
        return await _contentTypeRepository.GetAll();
    }

    public async Task<ContentType> GetById(Guid id)
    {
        return await _contentTypeRepository.GetById(id) ?? throw new ApplicationException("Requested contentType does not exists.");
    }

    public async Task<ContentType?> GetBySlug(string slug)
    {
        return await _contentTypeRepository.GetBySlug(slug);
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
