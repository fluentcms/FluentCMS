namespace FluentCMS.Web.Plugins.Admin.ApiTokenManagement;

public partial class ApiTokenListPlugin
{
    private List<ApiTokenDetailResponse> ApiTokens { get; set; } = [];
    private ApiTokenDetailResponse? SelectedApiToken { get; set; } = default!;
    private string Mode { get; set; } = "Delete";

    protected override async Task OnInitializedAsync()
    {
        var apiTokensResponse = await ApiClient.ApiToken.GetAllAsync();
        ApiTokens = apiTokensResponse?.Data?.ToList() ?? [];
    }

    private async Task OnRegenerateConfirm(ApiTokenDetailResponse token)
    {
        SelectedApiToken = token;
        Mode = "Regenerate";
        // 
    }
    // OnRegenerateConfirm

    private async Task OnConfirm(ApiTokenDetailResponse token)
    {
        SelectedApiToken = token;
        Mode = "Delete";
    }

    private async Task OnRegenerate()
    {
        await ApiClient.ApiToken.RegenerateSecretAsync(SelectedApiToken.Id);
        SelectedApiToken = default!;
        
    }

    private async Task OnDelete()
    {
        await ApiClient.ApiToken.DeleteAsync(SelectedApiToken.Id);
        SelectedApiToken = default!;
    }

    private async Task OnConfirmCancel()
    {
        SelectedApiToken = default!;
    }  
    
    private async Task OnRegenerateCancel()
    {
        SelectedApiToken = default!;
    }
}
