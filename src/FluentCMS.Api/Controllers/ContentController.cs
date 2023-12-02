using FluentCMS.Api.Models;
using FluentCMS.Entities;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Api.Controllers;

public class ContentController : BaseController
{
    [HttpPost]
    public async Task<IApiResult<Content>> Create(Content content, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        return new ApiResult<Content>(content);
    }

    [HttpPost]
    public async Task<IApiResult<Dictionary<string, object>>> CreateDic(Dictionary<string, object> content, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        return new ApiResult<Dictionary<string, object>>(content);
    }
}
