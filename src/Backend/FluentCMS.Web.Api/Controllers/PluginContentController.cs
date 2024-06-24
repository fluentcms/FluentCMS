namespace FluentCMS.Web.Api.Controllers;

[Route("api/[controller]/{pluginContentTypeName}/[action]")]
public class PluginContentController(IPluginContentService pluginContentService) : BaseGlobalController
{

    [HttpGet]
    [PolicyAll]
    public async Task<IApiPagingResult<PluginContentValue>> GetAll([FromRoute] string pluginContentTypeName, [FromQuery, Required] Guid pluginId, CancellationToken cancellationToken = default)
    {
        var contents = await pluginContentService.GetByPluginId(pluginId, cancellationToken);
        return OkPaged(contents.Select(x => x.Value).ToList());
    }

    [HttpGet("{id}")]
    [PolicyAll]
    public async Task<IApiResult<PluginContentValue>> GetById([FromRoute] string pluginContentTypeName, [FromRoute, Required] Guid id, CancellationToken cancellationToken = default)
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

        // check if dictionary has id key, remove it
        pluginContent.Value.Remove("id");

        await pluginContentService.Create(pluginContent, cancellationToken);

        return Ok(pluginContent.Value);
    }

    [HttpPut]
    [PolicyAll]
    public async Task<IApiResult<PluginContentValue>> Update([FromRoute] string pluginContentTypeName, [FromQuery, Required] Guid pluginId, [FromBody] PluginContentValue request, CancellationToken cancellationToken = default)
    {
        var pluginContent = new PluginContent
        {
            PluginId = pluginId,
            Type = pluginContentTypeName,
            Value = request
        };

        // check if dictionary has id key, set the id property and remove it
        // also check if the id is valid guid
        if (pluginContent.Value.TryGetValue("id", out object? value) && Guid.TryParse(value?.ToString() ?? string.Empty, out var id))
        {
            pluginContent.Id = id;
            pluginContent.Value.Remove("id");
        }
        else
        {
            throw new AppException(ExceptionCodes.PluginContentUnableToUpdate);
        }

        await pluginContentService.Update(pluginContent, cancellationToken);

        return Ok(pluginContent.Value);
    }

    [HttpDelete("{id}")]
    [PolicyAll]
    public async Task<IApiResult<bool>> Delete([FromRoute] string pluginContentTypeName, [FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        await pluginContentService.Delete(pluginContentTypeName, id, cancellationToken);
        return Ok(true);
    }

}
