using AutoMapper;
using FluentCMS.Api.Models;
using FluentCMS.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;

namespace FluentCMS.Api.Controllers;

public class AssetsController : BaseController
{
    private readonly IMapper _mapper;
    private readonly IAssetService _assetService;
    public AssetsController(IMapper mapper, IAssetService assetService)
    {
        _mapper = mapper;
        _assetService = assetService;
    }

    [HttpGet("{siteId:Guid}")]
    public async Task<IApiResult<IEnumerable<AssetResponse>>> GetAllSiteAssets([FromRoute] Guid siteId)
    {
        var sites = await _assetService.GetAllSiteAssets(siteId);
        var result = _mapper.Map<IEnumerable<AssetResponse>>(sites);
        return new ApiResult<IEnumerable<AssetResponse>>(result);
    }

    [HttpGet("{id:Guid}")]
    public async Task<IResult> Download([FromRoute] Guid id)
    {
        var (asset, stream) = await _assetService.GetAssetAsStream(id);
        return Results.File(stream, "application/octet-stream", asset.VirtualFileName);
    }

    [HttpGet("{id:Guid}")]
    public async Task<IResult> View([FromRoute] Guid id)
    {
        var (asset, stream) = await _assetService.GetAssetAsStream(id);
        var mimeTypeFound = new FileExtensionContentTypeProvider().TryGetContentType(asset.Extension, out var mimeType);
        if (!mimeTypeFound) mimeType = "application/octet-stream";
        return Results.File(stream, mimeType);
    }

    [HttpPost]
    public async Task<IApiResult<IEnumerable<UploadedFile>>> Upload(
        [FromForm] Guid siteId,
        [FromForm] string directory,
        [FromForm] IEnumerable<IFormFile> files)
    {
        // check max files count
        var allowedFilesCount = 5;
        if (files.Count() > allowedFilesCount)
            throw new ApplicationException($"You are only allowed to upload maximum {allowedFilesCount} files.");

        var result = new List<UploadedFile>();
        foreach (var file in files)
        {
            var filePath = $"{(string.IsNullOrWhiteSpace(directory) ? "" : directory + "/")}{file.FileName}";
            var uploadedFile = new UploadedFile
            {
                Id = Guid.Empty,
                FileName = file.Name,
                Successful = true,
            };

            try
            {
                var asset = await _assetService.AddFromStream(file.OpenReadStream(), filePath, siteId);
                uploadedFile.StoredFileName = asset.VirtualFileName;
                uploadedFile.Id = asset.Id;
            }
            catch (Exception ex)
            {
                uploadedFile.Successful = false;
                uploadedFile.Error = ex.Message;
            }
            result.Add(uploadedFile);
        }
        return new ApiResult<IEnumerable<UploadedFile>>(result);
    }

    [HttpDelete("{id:Guid}")]
    public async Task<IApiResult> Delete([FromRoute] Guid id)
    {
        await _assetService.DeleteAsset(id);
        return new ApiResult(true);
    }
}
