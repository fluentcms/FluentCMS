namespace FluentCMS.Web.UI;

public partial class SiteBuilderPreview
{
    [Inject]
    private ApiClientFactory ApiClient { get; set; } = default!;

    [Inject]
    public ViewState ViewState { get; set; } = default!;

    [Parameter]
    public RenderFragment? Body { get; set; }

    [Parameter]
    public RenderFragment? Head { get; set; }

    private List<PluginDefinitionDetailResponse> PluginDefinitions { get; set; } = [];

    protected override async Task OnInitializedAsync()
    {
        var response = await ApiClient.PluginDefinition.GetAllAsync();
        PluginDefinitions = response?.Data?.ToList() ?? [];
    }
}
