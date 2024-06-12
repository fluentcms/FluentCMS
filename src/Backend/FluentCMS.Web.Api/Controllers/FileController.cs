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

    //[HttpPost]
    //[Policy(AREA, CREATE)]
    //public async Task<IApiResult<FileDetailResponse>> Create([FromBody] FileCreateRequest request, CancellationToken cancellationToken = default)
    //{
    //    var asset = mapper.Map<Asset>(request);
    //    await fileService.Create(asset, cancellationToken);
    //    var assetResponse = mapper.Map<FileDetailResponse>(asset);
    //    return Ok(assetResponse);
    //}

    [HttpPost("{folderId}")]
    [Policy(AREA, CREATE)]
    public async Task<IApiResult<FileDetailResponse>> Upload([FromRoute] Guid folderId, List<IFormFile> files, CancellationToken cancellationToken = default)
    {
        long size = files.Sum(f => f.Length);

        foreach (var formFile in files)
        {
            if (formFile.Length > 0)
            {
                var filePath = Path.GetTempFileName();

                using (var stream = System.IO.File.Create(filePath))
                {
                    await formFile.CopyToAsync(stream);
                }
            }
        }
        return new ApiResult<FileDetailResponse>(new FileDetailResponse());
    }


    [HttpDelete("{id}")]
    [Policy(AREA, DELETE)]
    public async Task<IApiResult<bool>> Delete([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        await fileService.Delete(id, cancellationToken);
        return Ok(true);
    }
}
