namespace FluentCMS.Web.UI.Plugins.ApiTokenManagement;

public partial class ApiTokenListPlugin
{
    private List<ApiTokenDetailResponse> ApiTokens { get; set; } = [];

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        var apiTokensResponse = await GetApiClient<ApiTokenClient>().GetAllAsync();
        ApiTokens = apiTokensResponse?.Data?.ToList() ?? [];
    }
}