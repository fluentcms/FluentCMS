namespace FluentCMS.Web.UI.Plugins;

public partial class SiteListPlugin
{
    private List<SiteDetailResponse> Sites { get; set; } = [];
    private List<LayoutDetailResponse> Layouts { get; set; } = [];

    public async Task Load()
    {
        var sitesResponse = await GetApiClient<SiteClient>().GetAllAsync();
        Sites = sitesResponse?.Data?.ToList() ?? [];
    }

    protected override async Task OnInitializedAsync()
    {
        var layoutsResponse = await GetApiClient<LayoutClient>().GetAllAsync();
        Layouts = layoutsResponse?.Data?.ToList() ?? [];
        await Load();
    }

    #region Delete Site

    private SiteDetailResponse? SelectedSite { get; set; }
    public async Task OnDelete()
    {
        if (SelectedSite == null)
            return;

        await GetApiClient<SiteClient>().DeleteAsync(SelectedSite.Id);
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
