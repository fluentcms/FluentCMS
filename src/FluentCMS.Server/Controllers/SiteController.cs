using FluentCMS.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Server.Controllers;

public class SiteController : BaseController
{
    private readonly ISiteService _siteService;

    public SiteController(ISiteService siteService)
    {
        _siteService = siteService;
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<IResult<SiteDto>>> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var siteDto = await _siteService.GetById(id, cancellationToken);
        return new Result<SiteDto>
        {
            Data = siteDto,
            //Code = siteDto == null ? 404 : 200,
            //TraceId = HttpContext.TraceIdentifier,
            //Debug = new List<object> { new { url } },
            //Duration = 0,
            //Errors = new List<Error> { new Error { Message = "Site not found" } },
            //SessionId = HttpContext.Session.Id
        };
    }

    [HttpGet]
    public async Task<IResult<SiteDto>> GetByUrl([FromQuery] string url, CancellationToken cancellationToken = default)
    {
        var siteDto = await _siteService.GetByUrl(url, cancellationToken);
        return new Result<SiteDto>
        {
            Data = siteDto,
            //Code = siteDto == null ? 404 : 200,
            //TraceId = HttpContext.TraceIdentifier,
            //Debug = new List<object> { new { url } },
            //Duration = 0,
            //Errors = new List<Error> { new Error { Message = "Site not found" } },
            //SessionId = HttpContext.Session.Id
        };
    }

    //[HttpPost]
    //public async Task<ActionResult<ApiResult<Guid>>> Create(CreateSiteRequest request)
    //{
    //    var result = await siteService.Create(request);
    //    return SuccessResult(result);
    //}

    //[HttpPatch]
    //public async Task<ActionResult<ApiResult<bool>>> Edit(EditSiteRequest request)
    //{
    //    await siteService.Edit(request);
    //    return SuccessResult(true);
    //}

    //[HttpDelete("{id}")]
    //public async Task<ActionResult<ApiResult<bool>>> Delete([FromRoute] Guid id)
    //{
    //    await _siteService.(request);
    //    return SuccessResult(true);
    //}

}
