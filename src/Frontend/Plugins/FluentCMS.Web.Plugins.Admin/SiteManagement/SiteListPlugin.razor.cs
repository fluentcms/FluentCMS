namespace FluentCMS.Web.Plugins.Admin.SiteManagement;

public partial class SiteListPlugin
{
    private List<SiteDetailResponse> Sites { get; set; } = [];
    private List<LayoutDetailResponse> Layouts { get; set; } = [];

    public async Task Load()
    {
        var sitesResponse = await ApiClient.Site.GetAllAsync();
        Sites = sitesResponse?.Data?.ToList() ?? [];
    }

    protected override async Task OnInitializedAsync()
    {
        var layoutsResponse = await ApiClient.Layout.GetAllAsync(ViewState.Site.Id);
        Layouts = layoutsResponse?.Data?.ToList() ?? [];
        await Load();
    }

    #region Delete Site

    private SiteDetailResponse? SelectedSite { get; set; }
    public async Task OnDelete()
    {
        if (SelectedSite == null)
            return;

        await ApiClient.Site.DeleteAsync(SelectedSite.Id);
        await Load();
        SelectedSite = default;
    }

    public async Task OnConfirm(SiteDetailResponse site)
    {
        SelectedSite = site;
        await Task.CompletedTask;
    }
    public async Task OnConfirmClose()
    {
        SelectedSite = default;
        await Task.CompletedTask;
    }
    #endregion

}
