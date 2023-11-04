using FluentCMS.Application.Dtos.Sites;
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

    [HttpGet]
    public async Task<IListResult<SiteDto>> GetAll(CancellationToken cancellationToken = default)
    {
        var siteDtos = await _siteService.GetAll(cancellationToken);
        return new ListResult<SiteDto>(siteDtos);
    }

    [HttpGet("{id}")]
    public async Task<IResult<SiteDto>> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var siteDto = await _siteService.GetById(id, cancellationToken);
        return new Result<SiteDto>(siteDto);
    }

    [HttpGet]
    public async Task<IResult<SiteDto>> GetByUrl([FromQuery] string url, CancellationToken cancellationToken = default)
    {
        // TODO: should we change Url to domain?
        var siteDto = await _siteService.GetByUrl(url, cancellationToken);
        return new Result<SiteDto>(siteDto);
    }

    [HttpPost]
    public async Task<IResult<SiteDto>> Create(CreateSiteDto request, CancellationToken cancellationToken = default)
    {
        var siteDto = await _siteService.Create(request, cancellationToken);
        return new Result<SiteDto>(siteDto);
    }

    [HttpPatch]
    public async Task<IResult<SiteDto>> Update(UpdateSiteDto request, CancellationToken cancellationToken = default)
    {
        var siteDto = await _siteService.Update(request, cancellationToken);
        return new Result<SiteDto>(siteDto);
    }

    [HttpDelete("{id}")]
    public async Task<IResult> Delete([FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        await _siteService.Delete(id, cancellationToken);
        return new Result();
    }

}
