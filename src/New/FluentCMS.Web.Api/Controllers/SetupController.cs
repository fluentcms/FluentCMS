using FluentCMS.Entities;
using FluentCMS.Services;
using FluentCMS.Web.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace FluentCMS.Web.Api.Controllers;

public class SetupController(SetupManager setupManager) : BaseSystemController
{

    [HttpGet]
    public async Task<IApiResult<bool>> IsInitialized()
    {
        return Ok(await setupManager.IsInitialized());
    }

    [HttpPost]
    public async Task<IApiResult<GlobalSettings>> Start()
    {
        return Ok(await setupManager.Start());
    }
}
