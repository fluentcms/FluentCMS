namespace FluentCMS.Web.UI.Plugins;

public partial class LayoutListPlugin
{
    private List<LayoutDetailResponse> Layouts { get; set; } = [];

    protected override async Task OnInitializedAsync()
    {
        var response = await GetApiClient<LayoutClient>().GetAllAsync();
        Layouts = response?.Data?.ToList() ?? [];
    }
}
