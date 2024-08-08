namespace FluentCMS.Web.UI;

public partial class SiteBuilderForms
{
    [Inject]
    private ApiClientFactory ApiClient { get; set; } = default!;

    [CascadingParameter]
    private ViewState ViewState { get; set; } = default!;

    [SupplyParameterFromForm(FormName = "DeletePluginForm")]
    private Guid DeletePluginModel { get; set; } = new();

    [SupplyParameterFromForm(FormName = "CreatePluginForm")]
    private PluginCreateRequest CreatePluginModel { get; set; } = new();

    [SupplyParameterFromForm(FormName = "UpdatePluginsForm")]
    private UpdatePluginsRequest UpdatePluginsModel { get; set; } = new();

    private async Task OnUpdatePluginsSubmit()
    {
        Console.WriteLine($"Update Plugins ${UpdatePluginsModel.Plugins.Count}");
        foreach (var plugin in UpdatePluginsModel.Plugins)
        {
            await ApiClient.Plugin.UpdateAsync(plugin);
        }
    }

    private async Task OnCreatePluginSubmit()
    {
        CreatePluginModel.PageId = ViewState.Page.Id;
        await ApiClient.Plugin.CreateAsync(CreatePluginModel);
    }

    private async Task OnDeletePluginSubmit()
    {
        await ApiClient.Plugin.DeleteAsync(DeletePluginModel);
    }

    class UpdatePluginsRequest
    {
        public bool Submitted { get; set; } = true;
        public List<PluginUpdateRequest> Plugins { get; set; } = [];
    }
}
