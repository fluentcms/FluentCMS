using FluentCMS.Web.UI.Admin.Api;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Net.Http;
using System.Threading.Tasks;

[ApiController]
[Route("api/Layouts")]
public class LayoutsController : ControllerBase
{
    private readonly ApiClientFactory _api;

    public LayoutsController(ApiClientFactory api)
    {
        _api = api;
    }

    [HttpDelete("Remove/{id}")]
    public async Task<IActionResult> Remove(Guid id)
    {
        var result = await _api.Layouts.DeleteAsync(id);

        return StatusCode(200);
    }
}