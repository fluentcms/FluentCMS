namespace FluentCMS.Web.Plugins.Admin.PluginDefinitionManagement;

public partial class DefinitionListPlugin
{
    private List<PluginDefinitionDetailResponse> PluginDefinitions { get; set; } = [];

    protected override async Task OnInitializedAsync()
    {
        var pluginDefinitionsResponse = await ApiClient.PluginDefinition.GetAllAsync();
        PluginDefinitions = pluginDefinitionsResponse?.Data?.ToList() ?? [];
    }
}
