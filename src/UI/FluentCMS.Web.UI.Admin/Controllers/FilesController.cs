using FluentCMS.Web.UI.Admin.Api;
using FluentCMS.Web.UI.Admin.Api.ApiModels;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.Threading.Tasks;

[ApiController]
[Route("api/Files")]
public class FilesController : ControllerBase
{
    private readonly ApiClientFactory _api;

    public FilesController(ApiClientFactory api)
    {
        _api = api;
    }

    [HttpPost("Upload")]
    public async Task<IActionResult> Upload([FromQuery] Guid folderId)
    {
        var files = Request.Form.Files
            .Select(f => new FileParameter { Data = f.OpenReadStream(), FileName = f.FileName, ContentType = f.ContentType })
            .ToList();

        var result = await _api.Files.UploadAsync(folderId, files);

        return StatusCode(200, result);
    }

    [HttpDelete("Remove/{id}")]
    public async Task<IActionResult> Remove(Guid id)
    {
        var result = await _api.Files.DeleteAsync(id);
        return Ok(result.Data);
    }

    [HttpPut("Move")]
    public async Task<IActionResult> Move(FileMoveRequest request)
    {
        var result = await _api.Files.MoveAsync(request);
        return Ok(result.Data);
    }

    [HttpPut("Rename")]
    public async Task<IActionResult> Rename(FileRenameRequest request)
    {
        var result = await _api.Files.RenameAsync(request);
        return Ok(result.Data);
    }
}
