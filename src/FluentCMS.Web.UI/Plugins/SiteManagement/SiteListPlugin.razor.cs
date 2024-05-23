namespace FluentCMS.Web.UI.Plugins;

public partial class SiteListPlugin
{
    private List<SiteDetailResponse> Sites { get; set; } = [];

    protected override async Task OnInitializedAsync()
    {
        var sitesResponse = await GetApiClient<SiteClient>().GetAllAsync();
        Sites = sitesResponse?.Data?.ToList() ?? [];
    }
}
