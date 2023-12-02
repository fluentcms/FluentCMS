using AutoMapper;
using FluentCMS.Api.Models;
using FluentCMS.Entities;
using FluentCMS.Services;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Api.Controllers;

public class ContentController(IContentService contentService) : BaseController
{
    [HttpPost]
    public async Task<IApiResult<Content>> Create(Content request, CancellationToken cancellationToken = default)
    {
        var content = await contentService.Create(request, cancellationToken);
        return new ApiResult<Content>(content);
    }

    [HttpPost]
    public async Task<IApiResult<Dictionary<string, object>>> CreateDic(Dictionary<string, object> content, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        return new ApiResult<Dictionary<string, object>>(content);
    }

    [HttpPut]
    public async Task<IApiResult<Content>> Update(Content request, CancellationToken cancellationToken = default)
    {
        var content = await contentService.Update(request, cancellationToken);
        return new ApiResult<Content>(content);
    }

    [HttpDelete]
    public async Task<BooleanResponse> Delete(IdRequest request, CancellationToken cancellationToken = default)
    {
        await contentService.Delete(request.Id, cancellationToken);
        return new BooleanResponse(true);
    }
}
