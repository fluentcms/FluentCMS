using FluentCMS.Web.Api.Models.File;
using File = FluentCMS.Entities.File;

namespace FluentCMS.Web.Api.Controllers;

public class FileController(IFileService fileService, IMapper mapper) : BaseGlobalController
{
    [HttpPost]
    public async Task<IApiResult<FileDetailResponse>> Upload([FromForm] FileCreateRequest createRequest, CancellationToken cancellationToken = default)
    {
        var file = await fileService.Create(createRequest.File, cancellationToken: cancellationToken);
        return Ok(mapper.Map<File, FileDetailResponse>(file!));
    }

    [HttpGet]
    public async Task<IApiResult<IEnumerable<FileDetailResponse>>> GetFiles(CancellationToken cancellationToken = default)
    {
        var files = await fileService.GetAll(cancellationToken);
        return Ok(files.Select(file => mapper.Map<File, FileDetailResponse>(file!)));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetFileById([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var file = await fileService.GetById(id, cancellationToken);
        if (file == null)
        {
            return new NotFoundResult();
        }
        var fileStream = System.IO.File.OpenRead(file.LocalPath);
        return new FileStreamResult(fileStream, file.MimeType)
        {
            FileDownloadName = file.Name
        };
    }

    [HttpDelete("{id:guid}")]
    public async Task<IApiResult<bool>> DeleteById([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        var file = await fileService.DeleteById(id, cancellationToken);
        return Ok(file != null);
    }
}
