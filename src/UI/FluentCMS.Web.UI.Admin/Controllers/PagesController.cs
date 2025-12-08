using Admin.Api;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Net.Http;
using System.Threading.Tasks;

[ApiController]
[Route("api/Pages")]
public class PagesController : ControllerBase
{
    private readonly ApiClientFactory _api;

    public PagesController(ApiClientFactory api)
    {
        _api = api;
    }

    [HttpDelete("Remove/{id}")]
    public async Task<IActionResult> Remove(Guid id)
    {
        var result = await _api.Pages.DeleteAsync(id);

        return StatusCode(200);
    }
}