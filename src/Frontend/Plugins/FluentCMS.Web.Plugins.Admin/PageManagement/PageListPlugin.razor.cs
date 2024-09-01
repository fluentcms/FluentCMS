namespace FluentCMS.Web.Plugins.Admin.PageManagement;

public partial class PageListPlugin
{
    private List<PageDetailResponse> Pages { get; set; } = [];
    private List<LayoutDetailResponse> Layouts { get; set; } = [];

    [SupplyParameterFromQuery(Name = "id")]
    private Guid? Id { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "redirectTo")]
    private string? RedirectTo { get; set; } = default!;

    public async Task Load()
    {
        if (Id != null)
        {
            NavigateTo(GetUrl("Update Page", new { Id = Id, redirectTo = RedirectTo }, false));
        }
        var pagesResponse = await ApiClient.Page.GetAllAsync(ViewState.Site.Urls[0]);
        Pages = pagesResponse?.Data?.Where(x => !x.Locked).ToList() ?? [];
    }

    protected override async Task OnInitializedAsync()
    {
        var layoutsResponse = await ApiClient.Layout.GetAllAsync();
        Layouts = layoutsResponse?.Data?.ToList() ?? [];
        await Load();
    }

    private string GetPageUrl(PageDetailResponse page)
    {
        var result = new List<string>();
        var currentPage = page;
        while (currentPage != null)
        {
            result.Add(currentPage.Path);
            if (currentPage.ParentId.HasValue)
            {
                currentPage = Pages.Where(x => x.Id == currentPage.ParentId.Value).FirstOrDefault();
            }
            else
            {
                currentPage = default!;
            }
        }
        result.Reverse();

        return string.Join("", result);
    }

    #region Delete Page

    private PageDetailResponse? SelectedPage { get; set; }
    public async Task OnDelete()
    {
        if (SelectedPage == null)
            return;

        await ApiClient.Page.DeleteAsync(SelectedPage.Id);
        await Load();
        SelectedPage = default;
    }

    public async Task OnConfirm(PageDetailResponse page)
    {
        SelectedPage = page;
        await Task.CompletedTask;
    }
    public async Task OnConfirmClose()
    {
        SelectedPage = default;
        await Task.CompletedTask;
    }
    #endregion

}
