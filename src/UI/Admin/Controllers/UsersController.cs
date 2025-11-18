using Microsoft.AspNetCore.Mvc;
using System;
using System.Net.Http;
using System.Threading.Tasks;

[ApiController]
[Route("api/Users")]
public class UsersController : ControllerBase
{
    private readonly HttpClient _http;

    public UsersController(IHttpClientFactory factory)
    {
        _http = factory.CreateClient("BackendApi");
    }

    [HttpDelete("Remove/{id}")]
    public async Task<IActionResult> Remove(Guid id)
    {
        var result = await _http.DeleteAsync($"/api/Users/Remove/{id}");

        if (result.IsSuccessStatusCode)
            return Ok();

        return StatusCode((int)result.StatusCode);
    }
}