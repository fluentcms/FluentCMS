namespace FluentCMS.Web.Plugins.Admin.PluginDefinitionManagement;

public partial class DefinitionListPlugin
{
    private List<PluginDefinitionDetailResponse> PluginDefinitions { get; set; } = [];

    public async Task Load()
    { 
        var pluginDefinitionsResponse = await ApiClient.PluginDefinition.GetAllAsync();
        PluginDefinitions = pluginDefinitionsResponse?.Data?.Where(x => !x.Locked).ToList() ?? [];
    }

    protected override async Task OnInitializedAsync()
    {
        await Load();
    }
}
