namespace FluentCMS.Web.UI;

public partial class SiteBuilderPreview
{
    [Inject]
    public NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private ApiClientFactory ApiClient { get; set; } = default!;

    [Inject]
    public ViewState ViewState { get; set; } = default!;

    [Parameter]
    public RenderFragment Body { get; set; } = default!;

    [Parameter]
    public RenderFragment Head { get; set; } = default!;

    private List<PluginDefinitionDetailResponse> PluginDefinitions { get; set; } = [];

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var response = await ApiClient.PluginDefinition.GetAllAsync();
            PluginDefinitions = response.Data?.ToList() ?? [];
        }
        catch (Exception)
        {
            PluginDefinitions = [];
        }
    }

}