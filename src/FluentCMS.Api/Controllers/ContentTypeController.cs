using FluentCMS.Api.Models;
using FluentCMS.Entities;
using FluentCMS.Services;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Api.Controllers;

public class ContentTypeController(IContentTypeService contentTypeService) : BaseController
{
    [HttpGet]
    public async Task<IApiPagingResult<ContentType>> GetAll(CancellationToken cancellationToken = default)
    {
        var contentTypes = await contentTypeService.GetAll(cancellationToken);
        return new ApiPagingResult<ContentType>(contentTypes.ToList());
    }

    [HttpGet("{id}")]
    public async Task<IApiResult<ContentType>> GetById([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var contentType = await contentTypeService.GetById(id, cancellationToken);
        return new ApiResult<ContentType>(contentType);
    }

    [HttpGet]
    public async Task<IApiResult<ContentType>> GetByName([FromQuery] string name, CancellationToken cancellationToken = default)
    {
        var contentType = await contentTypeService.GetByName(name, cancellationToken);
        return new ApiResult<ContentType>(contentType);
    }

    [HttpPost]
    public async Task<IApiResult<ContentType>> Create(ContentType request, CancellationToken cancellationToken = default)
    {
        var contentType = await contentTypeService.Create(request, cancellationToken);
        return new ApiResult<ContentType>(contentType);
    }

    [HttpPut]
    public async Task<IApiResult<ContentType>> Update(ContentType request, CancellationToken cancellationToken = default)
    {
        var updated = await contentTypeService.Update(request, cancellationToken);
        return new ApiResult<ContentType>(updated);
    }

    [HttpDelete("{id}")]
    public async Task<IApiResult<bool>> Delete([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        await contentTypeService.Delete(id, cancellationToken);
        return new ApiResult<bool>(true);
    }

    [HttpPut("{id}")]
    public async Task<IApiResult<ContentType>> SetField([FromRoute] Guid id, ContentTypeField request, CancellationToken cancellationToken = default)
    {
        var updated = await contentTypeService.SetField(id, request, cancellationToken);
        return new ApiResult<ContentType>(updated);
    }

    [HttpPut("{id}/{name}")]
    public async Task<IApiResult<ContentType>> DeleteField([FromRoute] Guid id, [FromRoute] string name, CancellationToken cancellationToken = default)
    {
        var updated = await contentTypeService.RemoveField(id, name, cancellationToken);
        return new ApiResult<ContentType>(updated);
    }
}
