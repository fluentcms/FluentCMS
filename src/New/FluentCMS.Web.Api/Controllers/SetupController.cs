using FluentCMS.Entities;
using FluentCMS.Web.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Web.Api.Controllers;

public class SetupController(SetupManager setupManager) : BaseGlobalController
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

    [HttpPost]
    public async Task<IApiResult<bool>> Reset()
    {
        await setupManager.Reset();
        return Ok(await setupManager.IsInitialized());
    }
}
