using Admin.Api;
using Admin.Api.ApiModels;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Net.Http;
using System.Threading.Tasks;

[ApiController]
[Route("api/Folders")]
public class FoldersController : ControllerBase
{
    private readonly ApiClientFactory _api;

    public FoldersController(ApiClientFactory api)
    {
        _api = api;
    }

    [HttpDelete("Remove/{id}")]
    public async Task<IActionResult> Remove(Guid id)
    {
        var result = await _api.Folders.DeleteAsync(id);

        return StatusCode(200);
    }

    [HttpPost("Create")]
    public async Task<IActionResult> Create(FolderAddRequest request)
    {
        var result = await _api.Folders.CreateAsync(request);

        return StatusCode(200);
    }

    [HttpPut("Move")]
    public async Task<IActionResult> Move(FolderMoveRequest request)
    {
        var result = await _api.Folders.MoveAsync(request);

        return StatusCode(200);
    }

    [HttpPut("Rename")]
    public async Task<IActionResult> Rename(FolderRenameRequest request)
    {
        var result = await _api.Folders.RenameAsync(request);

        return StatusCode(200);
    }
}