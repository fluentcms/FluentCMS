namespace FluentCMS.Web.UI;
public partial class PageEditorSidebar
{
    [Inject]
    private ApiClientFactory ApiClient { get; set; } = default!;

    private ICollection<PluginDefinitionDetailResponse>? PluginDefinitions { get; set; } = default!;
    private ICollection<PageDetailResponse>? Pages { get; set; } = default!;

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

        // try {
            // TODO: Site url
            var pagesResponse = await ApiClient.Page.GetAllAsync("localhost:5000");
            Pages = pagesResponse.Data;
        // }
        // catch (Exception)
        // {
        //     Pages = [];
        // }
    }
}
