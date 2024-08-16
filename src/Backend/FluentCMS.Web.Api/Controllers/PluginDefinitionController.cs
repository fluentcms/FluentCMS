namespace FluentCMS.Web.Api.Controllers;


public class PluginDefinitionController(IMapper mapper, IPluginDefinitionService pluginDefinitionService) : BaseGlobalController
{
    public const string AREA = "Plugin Definition Management";
    public const string READ = "Read";
    public const string CREATE = "Create";

    [HttpPost]
    [Policy(AREA, CREATE)]
    public async Task<IApiResult<PluginDefinitionDetailResponse>> Create([FromBody] PluginDefinitionCreateRequest request, CancellationToken cancellationToken = default)
    {
        var entity = mapper.Map<PluginDefinition>(request);
        var created = await pluginDefinitionService.Create(entity, cancellationToken);
        var response = mapper.Map<PluginDefinitionDetailResponse>(created);
        return Ok(response);
    }

    [HttpGet]
    [Policy(AREA, READ)]
    public async Task<IApiPagingResult<PluginDefinitionDetailResponse>> GetAll(CancellationToken cancellationToken = default)
    {
        var entities = await pluginDefinitionService.GetAll(cancellationToken);
        var entitiesResponse = mapper.Map<List<PluginDefinitionDetailResponse>>(entities);
        return OkPaged(entitiesResponse);
    }
}
