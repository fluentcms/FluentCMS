namespace FluentCMS.Web.UI;

public partial class PageEditorOffcanvas
{
    [Inject]
    private ApiClientFactory ApiClient { get; set; } = default!;

    [CascadingParameter]
    private ViewState ViewState { get; set; } = default!;

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
            var response = await ApiClient.Layout.GetAllAsync();
            Layouts = response.Data;
        }
        catch (Exception)
        {
            Layouts = [];
        }

        try {
            // TODO: Site url
            Console.WriteLine($"Site URL {ViewState.Site.Urls[0]}");
            var response = await ApiClient.Page.GetAllAsync(ViewState.Site.Urls[0]);
            Pages = response.Data.Where(x => !x.Locked).ToList();
        }
        catch (Exception)
        {
            Pages = [];
        }
    }
}
