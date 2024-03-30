using FluentCMS.Web.Api.Models.File;
using File = FluentCMS.Entities.File;

namespace FluentCMS.Web.Api.Controllers;

public class FileController(IFileService fileService, IMapper mapper) : BaseGlobalController
{
    [HttpPost]
    public async Task<IApiResult<FileDetailResponse>> Upload([FromForm] FileCreateRequest createRequest, CancellationToken cancellationToken = default)
    {
        var file = await fileService.Create(createRequest.Slug, createRequest.File, cancellationToken: cancellationToken);
        return Ok(mapper.Map<File, FileDetailResponse>(file!));
    }

    [HttpGet]
    public async Task<IApiResult<IEnumerable<FileDetailResponse>>> GetFiles()
    {
        var files = await fileService.GetAll();
        return Ok(files.Select(file => mapper.Map<File, FileDetailResponse>(file!)));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetFileById([FromRoute] Guid id)
    {
        var file = await fileService.GetById(id);
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
    [HttpGet("{slug}")]
    public async Task<IActionResult> GetFileBySlug([FromRoute] string slug)
    {
        var file = await fileService.GetBySlug(slug);
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
    public async Task<IApiResult<bool>> DeleteById([FromRoute] Guid id)
    {
        var file = await fileService.DeleteById(id);
        return Ok(file != null);
    }
    [HttpDelete("{slug}")]
    public async Task<IApiResult<bool>> DeleteBySlug([FromRoute] string slug)
    {
        var file = await fileService.DeleteBySlug(slug);
        return Ok(file != null);
    }

}
