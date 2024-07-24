namespace FluentCMS.Web.UI;
public partial class PageEditorSidebar
{
    [Inject]
    private ApiClientFactory ApiClient { get; set; } = default!;

    [CascadingParameter]
    private ViewState ViewState { get; set; } = default!;

    private ICollection<PluginDefinitionDetailResponse>? PluginDefinitions { get; set; } = [];

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var response = await ApiClient.PluginDefinition.GetAllAsync();
            PluginDefinitions = response.Data;
        }
        catch (Exception)
        {
            PluginDefinitions = [];
        }
    }
}
