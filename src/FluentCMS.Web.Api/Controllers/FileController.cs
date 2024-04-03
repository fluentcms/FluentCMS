using FluentCMS.Web.Api.Models.File;
using File = FluentCMS.Entities.File;

namespace FluentCMS.Web.Api.Controllers;

public class FileController(IFileService fileService, IMapper mapper) : BaseGlobalController
{
    [HttpPost]
    public async Task<IApiResult<FileDetailResponse>> UploadSingle([FromForm] FileUploadSingleRequest createRequest, CancellationToken cancellationToken = default)
    {
        var file = await fileService.Create(createRequest.File, cancellationToken: cancellationToken);
        return Ok(mapper.Map<File, FileDetailResponse>(file));
    }

    [HttpPost]
    public async Task<IApiResult<IEnumerable<FileDetailResponse>>> UploadMultiple([FromForm] FileUploadMultipleRequest createRequest, CancellationToken cancellationToken = default)
    {
        var createdFiles = new List<File>();
        foreach (var requestFile in createRequest.Files)
        {
            createdFiles.Add(await fileService.Create(requestFile, cancellationToken: cancellationToken));
        }
        return Ok(createdFiles.Select(mapper.Map<File, FileDetailResponse>));
    }

    [HttpGet]
    public async Task<IApiResult<IEnumerable<FileDetailResponse>>> GetAll(CancellationToken cancellationToken = default)
    {
        var files = await fileService.GetAll(cancellationToken);
        return Ok(files.Select(file => mapper.Map<File, FileDetailResponse>(file!)));
    }

    [HttpGet("{id:guid}")]
    public async Task<IApiResult<FileDetailResponse>> GetById([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var file = await fileService.GetById(id, cancellationToken);
        return Ok(mapper.Map<File, FileDetailResponse>(file!));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Download([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var file = await fileService.GetById(id, cancellationToken);
        if (file == null)
        {
            return new NotFoundResult();
        }
        var fileStream = System.IO.File.OpenRead(fileService.GetFilePath(id));
        return new FileStreamResult(fileStream, file.MimeType)
        {
            FileDownloadName = file.Name
        };
    }

    [HttpDelete("{id:guid}")]
    public async Task<IApiResult<bool>> Delete([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var file = await fileService.Delete(id, cancellationToken);
        return Ok(file != null);
    }
}
