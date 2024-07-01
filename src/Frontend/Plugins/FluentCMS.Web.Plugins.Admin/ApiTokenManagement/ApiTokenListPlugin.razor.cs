namespace FluentCMS.Web.Plugins.Admin.ApiTokenManagement;

public partial class ApiTokenListPlugin
{
    private List<ApiTokenDetailResponse> ApiTokens { get; set; } = [];

    protected override async Task OnInitializedAsync()
    {
        var apiTokensResponse = await GetApiClient<ApiTokenClient>().GetAllAsync();
        ApiTokens = apiTokensResponse?.Data?.ToList() ?? [];
    }
}
