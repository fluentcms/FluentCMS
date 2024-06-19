namespace FluentCMS.Web.Api.Controllers;

[Route("api/[controller]/{pluginContentTypeName}/[action]")]
public class PluginContentController(IPluginContentService pluginContentService) : BaseGlobalController
{

    [HttpGet]
    public async Task<IApiPagingResult<PluginContentValue>> GetAll([FromRoute] string pluginContentTypeName, [FromQuery, Required] Guid pluginId, CancellationToken cancellationToken = default)
    {
        var contents = await pluginContentService.GetByPluginId(pluginId, cancellationToken);
        return OkPaged(contents.Select(x => x.Value).ToList());
    }

    [HttpGet("{id}")]
    [PolicyAll]
    public async Task<IApiResult<PluginContentValue>> GetById([FromRoute] string pluginContentTypeName, [FromQuery, Required] Guid id, CancellationToken cancellationToken = default)
    {
        var pluginContent = await pluginContentService.GetById(pluginContentTypeName, id, cancellationToken);
        return Ok(pluginContent.Value);
    }

    [HttpPost]
    [PolicyAll]
    public async Task<IApiResult<PluginContentValue>> Create([FromRoute] string pluginContentTypeName, [FromQuery, Required] Guid pluginId, [FromBody] PluginContentValue request, CancellationToken cancellationToken = default)
    {
        var pluginContent = new PluginContent
        {
            PluginId = pluginId,
            Type = pluginContentTypeName,
            Value = request
        };
        await pluginContentService.Create(pluginContent, cancellationToken);
        return Ok(pluginContent.Value);
    }

    [HttpPut]
    public async Task<IApiResult<PluginContentValue>> Update([FromRoute] string pluginContentTypeName, [FromQuery, Required] Guid pluginId, [FromBody] PluginContentValue request, CancellationToken cancellationToken = default)
    {
        var pluginContent = new PluginContent
        {
            PluginId = pluginId,
            Type = pluginContentTypeName,
            Value = request
        };
        await pluginContentService.Update(pluginContent, cancellationToken);
        return Ok(pluginContent.Value);
    }

    [HttpDelete("{id}")]
    public async Task<IApiResult<bool>> Delete([FromRoute] string pluginContentTypeName, [FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        await pluginContentService.Delete(pluginContentTypeName, id, cancellationToken);
        return Ok(true);
    }

}
