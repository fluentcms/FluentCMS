﻿using Microsoft.AspNetCore.Http;

namespace FluentCMS.Web.Api.Controllers;

public class FileController(IFileService fileService, IFolderService folderService, IMapper mapper) : BaseGlobalController
{
    public const string AREA = "Asset Management";
    public const string READ = "Read";
    public const string UPDATE = $"Update/{READ}";
    public const string CREATE = "Create";
    public const string DELETE = $"Delete/{READ}";

    [HttpGet("{id}")]
    [Policy(AREA, READ)]
    public async Task<IApiResult<FileDetailResponse>> GetById([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var file = await fileService.GetById(id, cancellationToken);
        var fileResponse = mapper.Map<FileDetailResponse>(file);
        return Ok(fileResponse);
    }

    [HttpPost]
    [Policy(AREA, CREATE)]
    public async Task<IApiPagingResult<FileDetailResponse>> Upload([FromQuery] Guid folderId, [FromForm] IEnumerable<IFormFile> files, CancellationToken cancellationToken = default)
    {
        var filesResponse = new List<FileDetailResponse>();

        var folder = await folderService.GetById(folderId, cancellationToken);

        foreach (var formFile in files.Where(x => x.Length > 0))
        {
            var file = new File
            {
                FolderId = folderId,
                SiteId = folder.SiteId,
                Name = formFile.FileName,
                Size = formFile.Length,
                ContentType = formFile.ContentType,
                Extension = System.IO.Path.GetExtension(formFile.FileName),
            };

            await fileService.Create(file, formFile.OpenReadStream(), cancellationToken);

            filesResponse.Add(mapper.Map<FileDetailResponse>(file));
        }

        return OkPaged(filesResponse);
    }

    [HttpGet("{id}")]
    [Policy(AREA, READ)]
    public async Task<IResult> Download([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var file = await fileService.GetById(id, cancellationToken);
        var fileStream = await fileService.GetStream(id, cancellationToken);
        return Results.File(fileStream, contentType: file.ContentType, fileDownloadName: file.Name, lastModified: file.ModifiedAt ?? file.CreatedAt);
    }

    [HttpGet]
    [Policy(AREA, READ)]
    public async Task<IResult> Download([FromQuery] string url, CancellationToken cancellationToken = default)
    {
        // example url = https://example.com/folder1/folder2/file.ext
        // extract folder path and file name from the url 
        var uri = new Uri(url);

        // Get the full path from the URL (without the scheme and domain)
        string fullPath = uri.AbsolutePath;

        // Extract the file name
        var fileName = System.IO.Path.GetFileName(fullPath);

        // Extract the folder path (excluding the file name)
        var folderPath = System.IO.Path.GetDirectoryName(fullPath) ?? "/";

        // find folders by url
        var folder = await folderService.GetByPath(folderPath, cancellationToken);

        // find file by name and folder id
        var file = await fileService.GetByName(folder.Id, fileName, cancellationToken);

        // get file stream
        var fileStream = await fileService.GetStream(folder.Id, cancellationToken);

        return Results.File(fileStream, contentType: file.ContentType, fileDownloadName: file.Name, lastModified: file.ModifiedAt ?? file.CreatedAt);
    }

    [HttpDelete("{id}")]
    [Policy(AREA, DELETE)]
    public async Task<IApiResult<bool>> Delete([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        await fileService.Delete(id, cancellationToken);
        return Ok(true);
    }

    [HttpPut]
    [Policy(AREA, UPDATE)]
    public async Task<IApiResult<FileDetailResponse>> Update([FromBody] FileUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var file = await fileService.GetById(request.Id, cancellationToken);
        //file.Name = request.Name;
        //file.FolderId = request.FolderId == Guid.Empty ? null : request.FolderId;

        //await fileService.Update(file, cancellationToken);

        var fileResponse = mapper.Map<FileDetailResponse>(file);
        return Ok(fileResponse);
    }
}
