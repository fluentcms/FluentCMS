using FluentCMS.Application.Sites;
using FluentCMS.Entities.Sites;
using FluentCMS.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using MediatR;
using FluentCMS.Models;

namespace FluentCMS.Shared.Controllers;
public class SiteController(IMediator mediator) : BaseController
{
    //GetAll
    [HttpGet]
    public async Task<ActionResult<ApiResult<IEnumerable<Site>>>> GetAll()
    {
        return SucessResult(await mediator.Send(new GetSitesQuery()));
    }

    //GetById
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResult<Site>>> GetById(Guid id)
    {
        return SucessResult(await mediator.Send(new GetSiteByIdQuery { Id = id }));
    }

    [HttpGet("[action]")]
    //GetByUrl
    public async Task<ActionResult<ApiResult<Site>>> GetByUrl([FromQuery] string url)
    {
        return SucessResult(await mediator.Send(new GetSiteByUrlQuery { Url = url }));
    }

    //Create
    [HttpPost()]
    public async Task<ActionResult<ApiResult<Guid>>> Create(CreateSiteCommand request)
    {
        return SucessResult(await mediator.Send(request));
    }

    //Update
    [HttpPatch]
    public async Task<ActionResult<ApiResult<Guid>>> Update(EditSiteCommand request)
    {
        return SucessResult(await mediator.Send(request));
    }

    [HttpDelete("{id}")]
    //Delete
    public async Task<ActionResult<ApiResult<Guid>>> Delete(Guid id)
    {
        return SucessResult(await mediator.Send(new DeleteSiteCommand { Id = id }));
    }

    [HttpPut("[action]")]
    //AddUrl
    public async Task<ActionResult> AddUrl(AddSiteUrlCommand request)
    {
        await mediator.Send(request);
        return Ok();
    }

    [HttpDelete("[action]")]
    //RemoveUrl
    public async Task<ActionResult> RemoveUrl(RemoveSiteUrlCommand request)
    {
        await mediator.Send(request);
        return Ok();
    }
}
