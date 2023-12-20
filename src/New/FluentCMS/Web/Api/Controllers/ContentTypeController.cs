using AutoMapper;
using FluentCMS.Entities;
using FluentCMS.Services;
using FluentCMS.Web.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Web.Api.Controllers;

[Route("{appSlug}/api/[controller]/[action]")]
public class ContentTypeController(
    IMapper mapper,
    IContentTypeService contentTypeService,
    AppService appService) : BaseController
{
    [HttpGet]
    public async Task<IApiPagingResult<ContentTypeResponse>> GetAll([FromRoute] string appSlug, CancellationToken cancellationToken = default)
    {
        var app = await appService.GetBySlug(appSlug, cancellationToken);
        var contentTypes = await contentTypeService.GetAll(app.Id, cancellationToken);
        var contentTypeResponses = mapper.Map<List<ContentTypeResponse>>(contentTypes);
        return OkPaged(contentTypeResponses);
    }

    [HttpPost]
    public async Task<IApiResult<ContentTypeResponse>> Create([FromRoute] string appSlug, [FromBody] ContentTypeCreateRequest request, CancellationToken cancellationToken = default)
    {
        var contentType = mapper.Map<ContentType>(request);
        var app = await appService.GetBySlug(appSlug, cancellationToken);
        contentType.AppId = app.Id;
        var newContentType = await contentTypeService.Create(contentType, cancellationToken);
        var response = mapper.Map<ContentTypeResponse>(newContentType);
        return Ok(response);
    }

    [HttpPut]
    public async Task<IApiResult<ContentTypeResponse>> Update([FromRoute] string appSlug, [FromBody] ContentTypeUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var contentType = mapper.Map<ContentType>(request);
        var app = await appService.GetBySlug(appSlug, cancellationToken);
        contentType.AppId = app.Id;
        var updated = await contentTypeService.Update(contentType, cancellationToken);
        var response = mapper.Map<ContentTypeResponse>(updated);
        return Ok(response);
    }

    [HttpDelete("{id}")]
    public async Task<IApiResult<bool>> Delete([FromRoute] string appSlug, [FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var app = await appService.GetBySlug(appSlug, cancellationToken);
        await contentTypeService.Delete(app.Id, id, cancellationToken);
        return Ok(true);
    }

    [HttpPut("{id}")]
    public async Task<IApiResult<ContentTypeResponse>> SetField([FromRoute] string appSlug, [FromRoute] Guid id, ContentTypeField request, CancellationToken cancellationToken = default)
    {
        var app = await appService.GetBySlug(appSlug, cancellationToken);
        var updated = await contentTypeService.SetField(app.Id, id, request, cancellationToken);
        var response = mapper.Map<ContentTypeResponse>(updated);
        return Ok(response);
    }

    [HttpDelete("{id}/{name}")]
    public async Task<IApiResult<ContentTypeResponse>> DeleteField([FromRoute] string appSlug, [FromRoute] Guid id, [FromRoute] string name, CancellationToken cancellationToken = default)
    {
        var app = await appService.GetBySlug(appSlug, cancellationToken);
        var updated = await contentTypeService.DeleteField(app.Id, id, name, cancellationToken);
        var response = mapper.Map<ContentTypeResponse>(updated);
        return Ok(response);
    }
}
