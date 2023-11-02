using FluentCMS.Application.Dtos.Sites;
using FluentCMS.Application.Services;
using FluentCMS.Models;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Web.Controllers;
public class SiteController(ISiteService siteService) : BaseController
{
    [HttpGet]
    public async Task<ActionResult<ApiResult<SearchSiteResponse>>> Search([FromQuery] SearchSiteRequest request)
    {
        return SuccessResult(await siteService.Search(request));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResult<SiteDto>>> GetById(Guid id)
    {
        return SuccessResult(await siteService.GetById(id));
    }

    [HttpGet("[action]")]
    public async Task<ActionResult<ApiResult<SiteDto>>> GetByUrl([FromQuery] string url)
    {
        return SuccessResult(await siteService.GetByUrl(url));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResult<Guid>>> Create(CreateSiteRequest request)
    {
        var result = await siteService.Create(request);
        return SuccessResult(result);
    }

    [HttpPatch]
    public async Task<ActionResult<ApiResult<bool>>> Edit(EditSiteRequest request)
    {
        await siteService.Edit(request);
        return SuccessResult(true);
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult<ApiResult<bool>>> Delete([FromRoute] DeleteSiteRequest request)
    {
        await siteService.Delete(request);
        return SuccessResult(true);
    }

    [HttpPut("[action]")]
    public async Task<ActionResult<ApiResult<bool>>> AddUrl(AddSiteUrlRequest request)
    {
        await siteService.AddSiteUrl(request);
        return SuccessResult(true);
    }

    [HttpDelete("[action]")]
    public async Task<ActionResult<ApiResult<bool>>> RemoveUrl(RemoveSiteUrlRequest request)
    {
        await siteService.RemoveSiteUrl(request);
        return SuccessResult(true);
    }
}
