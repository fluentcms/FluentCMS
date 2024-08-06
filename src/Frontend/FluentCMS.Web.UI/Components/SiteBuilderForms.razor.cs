namespace FluentCMS.Web.UI;

public partial class SiteBuilderForms
{
    [Inject]
    private ApiClientFactory ApiClient { get; set; } = default!;

    [CascadingParameter]
    private ViewState ViewState { get; set; } = default!;

    [SupplyParameterFromForm(FormName = "UpdatePluginForm")]
    private PageEditorSaveRequest Model { get; set; } = new();

    [SupplyParameterFromForm(FormName = "CreatePluginForm")]
    private PluginCreateRequest CreatePluginModel { get; set; } = new();

    private async Task OnUpdateSubmit()
    {
        foreach (var deletedId in Model.DeleteIds ?? [])
        {
            await ApiClient.Plugin.DeleteAsync(deletedId);
        }

        foreach (var plugin in Model.CreatePlugins ?? [])
        {
            plugin.PageId = ViewState.Page.Id;

            await ApiClient.Plugin.CreateAsync(plugin);
        }

        foreach (var plugin in Model.UpdatePlugins ?? [])
        {
            await ApiClient.Plugin.UpdateAsync(plugin);
        }
    }

    private async Task OnCreatePluginSubmit()
    {
        CreatePluginModel.PageId = ViewState.Page.Id;

        await ApiClient.Plugin.CreateAsync(CreatePluginModel);
    }

    class PageEditorSaveRequest
    {
        public bool Submitted { get; set; } = true;
        public List<Guid> DeleteIds { get; set; } = [];
        public List<PluginCreateRequest> CreatePlugins { get; set; } = [];
        public List<PluginUpdateRequest> UpdatePlugins { get; set; } = [];
    }
}
