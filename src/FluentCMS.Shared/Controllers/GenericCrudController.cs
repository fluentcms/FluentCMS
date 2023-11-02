using FluentCMS.Application;
using FluentCMS.Entities;
using FluentCMS.Models;
using FluentCMS.Shared.Controllers;
using FluentValidation;
using Microsoft.AspNetCore.JsonPatch;
using Microsoft.AspNetCore.Mvc;

namespace FluentCMS.Web.Controllers;

[ValidationActionFilter]
public abstract class GenericCrudController<T, TDto> : BaseController
    where T : IEntity
    where TDto : class
{
    private readonly IServiceProvider _serviceProvider;
    private readonly GenericCrudServiceWithMapping<T, TDto> _service;

    public GenericCrudController(IServiceProvider serviceProvider, GenericCrudServiceWithMapping<T, TDto> service)
    {
        _serviceProvider = serviceProvider;
        _service = service;
    }

    [HttpGet]
    public async Task<ActionResult<ApiResult<IEnumerable<TDto>>>> GetAll()
    {

        return SuccessResult((await _service.GetAllAsync()).AsEnumerable());


    }
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResult<TDto>>> GetById([FromRoute] Guid id)
    {
        return SuccessResult(await _service.GetByIdAsync(id));

    }
    [HttpPost]
    public async Task<ActionResult> Create([FromBody] TDto dto)
    {

            await _service.CreateAsync(dto);
            return Ok();

    }
    [HttpPatch("{id}")]
    public async Task<ActionResult> Update([FromRoute] Guid id, [FromBody] JsonPatchDocument<TDto> dto)
    {

            await _service.UpdateAsync(id, dto);
            return Ok();


    }
    [HttpPatch()]
    public async Task<ActionResult> Update([FromBody] TDto dto)
    {

            await _service.UpdateAsync(dto);
            return Ok();
    }
    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete([FromRoute] Guid id)
    {
        await _service.DeleteAsync(id);
        return Ok();
    }
}
