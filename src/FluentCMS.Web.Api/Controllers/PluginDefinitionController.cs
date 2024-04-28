using FluentCMS.Web.Api.Attributes;

namespace FluentCMS.Web.Api.Controllers;

public class PluginDefinitionController(IMapper mapper, IPluginDefinitionService pluginDefinitionService) : BaseGlobalController
{
    [HttpPost]
    [JwtAuthorize]
    public async Task<IApiResult<PluginDefinitionDetailResponse>> Create([FromBody] PluginDefinitionCreateRequest request, CancellationToken cancellationToken = default)
    {
        var entity = mapper.Map<PluginDefinition>(request);
        var created = await pluginDefinitionService.Create(entity, cancellationToken);
        var response = mapper.Map<PluginDefinitionDetailResponse>(created);
        return Ok(response);
    }

    [HttpGet]
    public async Task<IApiPagingResult<PluginDefinitionDetailResponse>> GetAll(CancellationToken cancellationToken = default)
    {
        var entities = await pluginDefinitionService.GetAll(cancellationToken);
        var entitiesResponse = mapper.Map<List<PluginDefinitionDetailResponse>>(entities);
        return OkPaged(entitiesResponse);
    }
}
