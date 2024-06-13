using Microsoft.AspNetCore.Http;

namespace FluentCMS.Web.Api.Controllers;

public class FileController(IFileService fileService, IMapper mapper) : BaseGlobalController
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
    public async Task<IApiPagingResult<FileDetailResponse>> Upload([FromRoute] Guid? folderId, [FromForm] IEnumerable<IFormFile> files, CancellationToken cancellationToken = default)
    {
        var filesResponse = new List<FileDetailResponse>();

        foreach (var formFile in files.Where(x => x.Length > 0))
        {
            var asset = await fileService.Create(folderId, formFile, cancellationToken);
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
