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
    public async Task<IApiPagingResult<FileDetailResponse>> Upload([FromQuery] Guid? folderId, [FromForm] IEnumerable<IFormFile> files, CancellationToken cancellationToken = default)
    {
        var filesResponse = new List<FileDetailResponse>();

        foreach (var formFile in files.Where(x => x.Length > 0))
        {
            var file = new File
            {
                FolderId = folderId,
                Name = formFile.FileName,
                Size = formFile.Length,
                ContentType = formFile.ContentType,
                Extension = System.IO.Path.GetExtension(formFile.FileName),
            };

            await fileService.Create(file, formFile.OpenReadStream(), cancellationToken);
            file.FolderId ??= Guid.Empty;
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
        file.Name = request.Name;
        file.FolderId = request.FolderId == Guid.Empty ? null : request.FolderId;

        await fileService.Update(file, cancellationToken);

        var fileResponse = mapper.Map<FileDetailResponse>(file);
        return Ok(fileResponse);
    }
}
