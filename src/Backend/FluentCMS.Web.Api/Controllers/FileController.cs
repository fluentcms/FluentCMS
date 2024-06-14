using Microsoft.AspNetCore.Http;

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
    public async Task<IApiPagingResult<FileDetailResponse>> Upload([FromQuery] Guid? folderId, [FromForm] IEnumerable<IFormFile> files, CancellationToken cancellationToken = default)
    {
        // check if parent folder exists
        if (folderId != null)
        {
            await folderService.GetById(folderId.Value, cancellationToken);
        }

        var filesResponse = new List<FileDetailResponse>();

        foreach (var formFile in files.Where(x => x.Length > 0))
        {
            var asset = new Asset
            {
                FolderId = folderId,
                Name = formFile.FileName,
                Size = formFile.Length,
                Type = AssetType.File,
                MetaData = new AssetMetaData
                {
                    Extension = Path.GetExtension(formFile.FileName),
                    MimeType = formFile.ContentType
                }
            };
            await fileService.Create(asset, formFile.OpenReadStream(), cancellationToken);
            filesResponse.Add(mapper.Map<FileDetailResponse>(asset));
        }

        return OkPaged(filesResponse);
    }

    [HttpGet("{id}")]
    [Policy(AREA, READ)]
    public async Task<IResult> Download([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var file = await fileService.GetById(id, cancellationToken);
        var fileStream = await fileService.GetStream(id, cancellationToken);
        return Results.File(fileStream, contentType: file.MetaData!.MimeType, fileDownloadName: file.Name, lastModified: file.ModifiedAt ?? file.CreatedAt);
    }

    [HttpDelete("{id}")]
    [Policy(AREA, DELETE)]
    public async Task<IApiResult<bool>> Delete([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        await fileService.Delete(id, cancellationToken);
        return Ok(true);
    }
}
