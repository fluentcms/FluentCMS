using FluentCMS.Web.Api.Attributes;
using Microsoft.AspNetCore.Http;

namespace FluentCMS.Web.Api.Controllers;

public class FileController(IFileProvider fileProvider, IMapper mapper) : BaseGlobalController
{
    public const string AREA = "File Management";
    public const string READ = "Read";
    public const string CREATE = "Create";
    public const string DOWNLOAD = $"Download/{READ}";
    public const string DELETE = $"Delete/{READ}";

    [HttpPost]
    [Policy(AREA, CREATE)]
    public async Task<IApiResult<FileDetailResponse>> Create([FromForm] FileCreateRequest request, CancellationToken cancellationToken = default)
    {
        var file = mapper.Map<IFormFile, FileEntity>(request.File);
        var createdFile = await fileProvider.Create(file, request.File.OpenReadStream(), cancellationToken);
        return Ok(mapper.Map<FileDetailResponse>(createdFile));
    }

    [HttpPost]
    [Policy(AREA, CREATE)]
    public async Task<IApiPagingResult<FileDetailResponse>> CreateMultiple([FromForm] FileCreateMultipleRequest request, CancellationToken cancellationToken = default)
    {
        var files = new List<FileEntity>();
        foreach (var requestFile in request.Files)
        {
            files.Add(await fileProvider.Create(mapper.Map<IFormFile, FileEntity>(requestFile), requestFile.OpenReadStream(), cancellationToken));
        }

        return OkPaged(files.Select(mapper.Map<FileDetailResponse>));
    }

    [HttpGet]
    [Policy(AREA, READ)]
    public async Task<IApiPagingResult<FileDetailResponse>> GetAll(CancellationToken cancellationToken = default)
    {
        var files = await fileProvider.GetAll(cancellationToken);
        var fileDetailResponses = files.Select(x => mapper.Map<FileDetailResponse>(x)).ToList();
        return OkPaged(fileDetailResponses);
    }

    [HttpGet("{id:guid}")]
    [Policy(AREA, READ)]
    public async Task<IApiResult<FileDetailResponse>> Get([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var file = (await fileProvider.GetById(id, cancellationToken));
        var fileDetailResponse = mapper.Map<FileDetailResponse>(file);
        return Ok(fileDetailResponse);
    }

    [HttpGet("{id:guid}")]
    [Policy(AREA, DOWNLOAD)]
    public async Task<IActionResult> Download([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var file = await fileProvider.GetById(id, cancellationToken);
        var fileStream = await fileProvider.GetStream(id, cancellationToken);
        return new FileStreamResult(fileStream, file.ContentType)
        {
            FileDownloadName = file.FileName
        };
    }

    [HttpGet("{id:guid}")]
    [Policy(AREA, DELETE)]
    public async Task<IApiResult<bool>> Delete([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        await fileProvider.Delete(id, cancellationToken);
        return Ok(true);
    }
}
