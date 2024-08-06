namespace FluentCMS.Web.Api.Controllers;

[Route("api/[controller]/{pluginContentTypeName}/[action]")]
public class PluginContentController(IPluginContentService pluginContentService, IMapper mapper) : BaseGlobalController
{

    public const string AREA = "Plugin Content Management";

    [HttpGet("{pluginId}")]
    [PolicyAll]
    public async Task<IApiPagingResult<PluginContentDetailResponse>> GetAll([FromRoute] string pluginContentTypeName, [FromRoute, Required] Guid pluginId, CancellationToken cancellationToken = default)
    {
        var contents = await pluginContentService.GetByPluginId(pluginId, cancellationToken);
        var response = mapper.Map<List<PluginContentDetailResponse>>(contents.ToList());
        return OkPaged(response);
    }

    [HttpGet("{pluginId}/{id}")]
    [PolicyAll]
    public async Task<IApiResult<PluginContentDetailResponse>> GetById([FromRoute] string pluginContentTypeName, [FromRoute, Required] Guid pluginId, [FromRoute, Required] Guid id, CancellationToken cancellationToken = default)
    {
        var pluginContent = await pluginContentService.GetById(pluginContentTypeName, id, cancellationToken);
        var response = mapper.Map<PluginContentDetailResponse>(pluginContent);
        return Ok(response);
    }

    [HttpPost("{pluginId}")]
    [PolicyAll]
    public async Task<IApiResult<PluginContentDetailResponse>> Create([FromRoute] string pluginContentTypeName, [FromRoute, Required] Guid pluginId, [FromBody] Dictionary<string, object?> request, CancellationToken cancellationToken = default)
    {
        var pluginContent = new PluginContent
        {
            PluginId = pluginId,
            Type = pluginContentTypeName,
            Data = request
        };

        await pluginContentService.Create(pluginContent, cancellationToken);

        var response = mapper.Map<PluginContentDetailResponse>(pluginContent);

        return Ok(response);
    }

    [HttpPut("{pluginId}/{id}")]
    [PolicyAll]
    public async Task<IApiResult<PluginContentDetailResponse>> Update([FromRoute] string pluginContentTypeName, [FromRoute, Required] Guid pluginId, [FromRoute, Required] Guid id, [FromBody] Dictionary<string, object?> request, CancellationToken cancellationToken = default)
    {
        var pluginContent = new PluginContent
        {
            Id = id,
            PluginId = pluginId,
            Type = pluginContentTypeName,
            Data = request
        };

        await pluginContentService.Update(pluginContent, cancellationToken);

        var response = mapper.Map<PluginContentDetailResponse>(pluginContent);

        return Ok(response);
    }

    [HttpDelete("{pluginId}/{id}")]
    [PolicyAll]
    public async Task<IApiResult<bool>> Delete([FromRoute] string pluginContentTypeName, [FromRoute, Required] Guid pluginId, [FromRoute] Guid id, CancellationToken cancellationToken = default)
    {
        await pluginContentService.Delete(pluginContentTypeName, id, cancellationToken);
        return Ok(true);
    }

}
