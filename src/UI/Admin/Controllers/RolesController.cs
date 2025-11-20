using Admin.Api;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Net.Http;
using System.Threading.Tasks;

[ApiController]
[Route("api/Roles")]
public class RolesController : ControllerBase
{
    private readonly ApiClientFactory _api;

    public RolesController(ApiClientFactory api)
    {
        _api = api;
    }

    [HttpDelete("Remove/{id}")]
    public async Task<IActionResult> Remove(Guid id)
    {
        var result = await _api.Roles.DeleteAsync(id);

        return StatusCode(200);
    }
}