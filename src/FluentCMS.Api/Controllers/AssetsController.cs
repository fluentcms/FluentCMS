using FluentCMS.Api.Models;
using FluentCMS.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Api.Controllers;

public class AssetsController : BaseController
{
    private readonly IAssetService _assetService;
    public AssetsController(IAssetService assetService)
    {
        _assetService = assetService;
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
}
