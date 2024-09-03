namespace FluentCMS.Web.UI;

public partial class SiteBuilder
{
    [Inject]
    public NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private ApiClientFactory ApiClient { get; set; } = default!;

    [CascadingParameter]
    public ViewState ViewState { get; set; } = default!;

    [Parameter]
    public RenderFragment Body { get; set; } = default!;

    [Parameter]
    public RenderFragment Head { get; set; } = default!;

    private List<PluginDefinitionDetailResponse> PluginDefinitions { get; set; } = [];
    private List<BlockDetailResponse> Blocks { get; set; } = [];

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var response = await ApiClient.PluginDefinition.GetAllAsync();
            PluginDefinitions = response.Data.ToList();

            var blocksResponse = await ApiClient.Block.GetAllForSiteAsync(ViewState.Site.Id);
            Blocks = blocksResponse.Data.ToList();
        }
        catch (Exception)
        {
            PluginDefinitions = [];
        }
    }

}