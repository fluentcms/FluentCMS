using FluentCMS.Api.Models;
using FluentCMS.Providers.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Api.Controllers;

public class FileStorageController : BaseController
{
    private readonly IStorageProvider _storagePlugin;
    public FileStorageController(IStorageProvider storagePlugin)
    {
        _storagePlugin = storagePlugin;
    }

    [HttpPost]
    public async Task<IApiResult<IEnumerable<UploadedFile>>> Upload([FromForm] IEnumerable<IFormFile> files)
    {
        // check max files count
        var allowedFilesCount = 5;
        if (files.Count() > allowedFilesCount)
            throw new ApplicationException($"You are only allowed to upload maximum {allowedFilesCount} files.");

        // allowed file types
        var allowedFileTypes = new[] { ".jpg", ".jpeg", ".png", ".pdf" };

        var result = new List<UploadedFile>();
        foreach (var file in files)
        {
            var fileId = Guid.NewGuid();
            var fileExt = Path.GetExtension(file.FileName);
            var filePath = $"unsafe/{fileId.ToString().ToLower()}{fileExt}";
            var uploadedFile = new UploadedFile
            {
                Id = fileId,
                FileName = file.Name,
                StoredFileName = filePath,
                Successful = true,
            };

            // check file type
            if (allowedFileTypes.Contains(fileExt) == false)
            {
                uploadedFile.Successful = false;
                uploadedFile.Error = $"Type {fileExt} is not allowed";
                continue;
            }

            try
            {
                await _storagePlugin.SaveFile(file.OpenReadStream(), filePath);
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
