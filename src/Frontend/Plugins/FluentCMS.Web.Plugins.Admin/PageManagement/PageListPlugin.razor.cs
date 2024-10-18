namespace FluentCMS.Web.Plugins.Admin.PageManagement;

public partial class PageListPlugin
{
    private List<PageDetailResponse> Pages { get; set; } = [];
    private List<LayoutDetailResponse> Layouts { get; set; } = [];

    public async Task Load()
    {
        var pagesResponse = await ApiClient.Page.GetAllAsync(ViewState.Site.Id);
        Pages = pagesResponse?.Data?.OrderBy(x => x.Order).ToList() ?? [];
    }

    protected override async Task OnInitializedAsync()
    {
        var layoutsResponse = await ApiClient.Layout.GetBySiteIdAsync(ViewState.Site.Id);
        Layouts = layoutsResponse?.Data?.ToList() ?? [];
        await Load();
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
