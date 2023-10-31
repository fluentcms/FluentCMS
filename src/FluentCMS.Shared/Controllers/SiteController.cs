using FluentCMS.Application.Sites;
using FluentCMS.Entities.Sites;
using FluentCMS.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using MediatR;

namespace FluentCMS.Shared.Controllers;
public class SiteController(IMediator mediator) : BaseController
{
    //GetAll
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Site>>> GetAll()
    {
        return Ok(await mediator.Send(new GetSitesQuery()));
    }

    //GetById
    [HttpGet("{id}")]
    public async Task<ActionResult<Site?>> GetById(Guid id)
    {
        return Ok(await mediator.Send(new GetSiteByIdQuery { Id = id }));
    }

    [HttpGet]
    //GetByUrl
    public async Task<ActionResult<Site?>> GetByUrl([FromQuery] string url)
    {
        return Ok(await mediator.Send(new GetSiteByUrl { Url = url }));
    }

    //Create
    [HttpPost()]
    public async Task<ActionResult<Guid>> Create(CreateSiteCommand request)
    {
        return Ok(await mediator.Send(request));
    }

    //Update
    [HttpPatch]
    public async Task<ActionResult<Guid>> Update(EditSiteCommand request)
    {
        return Ok(await mediator.Send(request));
    }

    [HttpDelete]
    //Delete
    public async Task<ActionResult<Guid>> Delete(Guid id)
    {
        return Ok(await mediator.Send(new DeleteSiteCommand { Id = id }));
    }

    [HttpPut]
    //AddUrl
    public async Task<ActionResult> AddUrl(AddUrlSiteCommand request)
    {
        await mediator.Send(request);
        return Ok();
    }

    [HttpDelete]
    //RemoveUrl
    public async Task<ActionResult> RemoveUrl(RemoveUrlSiteCommand request)
    {
        await mediator.Send(request);
        return Ok();
    }
}
