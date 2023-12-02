using AutoMapper;
using FluentCMS.Api.Models;
using FluentCMS.Entities;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Api.Controllers;

public class ContentController(IMapper mapper) : BaseController
{

    [HttpPost]
    public async Task<IApiResult<Content>> Create(Content content, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        return new ApiResult<Content>(content);
    }
}
