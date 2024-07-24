namespace FluentCMS.Web.UI;
public partial class PageEditorSidebar
{
    [Inject]
    private ApiClientFactory ApiClient { get; set; } = default!;

    [CascadingParameter]
    private ViewState ViewState { get; set; } = default!;

    private ICollection<PluginDefinitionDetailResponse>? PluginDefinitions { get; set; } = [];
    private ICollection<PageDetailResponse>? Pages { get; set; } = [];
    private ICollection<LayoutDetailResponse>? Layouts { get; set; } = [];

    private string GetPageUrl(PageDetailResponse page)
    {
        if (page.ParentId != null)
        {
            var parent = Pages.Where(x => x.Id == page.ParentId).FirstOrDefault();
            return GetPageUrl(parent) + page.Path;
        }
        return page.Path;
    } 

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

        try
        {
            var response = await ApiClient.Layout.GetAllAsync();
            Layouts = response.Data;
        }
        catch (Exception)
        {
            Layouts = [];
        }

        try {
            // TODO: Site url
            var response = await ApiClient.Page.GetAllAsync("localhost:5000");
            Pages = response.Data.Where(x => !x.Locked).ToList();
        }
        catch (Exception)
        {
            Pages = [];
        }
    }
}
