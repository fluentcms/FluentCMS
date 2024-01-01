﻿namespace FluentCMS.Web.Api.Controllers;

public class PluginDefinitionController(IMapper mapper, IPluginDefinitionService pluginDefinitionService) : BaseGlobalController
{
    [HttpPost]
    public async Task<IApiResult<PluginDefinitionDetailResponse>> Create([FromBody] PluginDefinitionCreateRequest request, CancellationToken cancellationToken = default)
    {
        var entity = mapper.Map<PluginDefinition>(request);
        var created = await pluginDefinitionService.Create(entity, cancellationToken);
        var response = mapper.Map<PluginDefinitionDetailResponse>(created);
        return Ok(response);
    }
}
