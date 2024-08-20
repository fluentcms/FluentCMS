namespace FluentCMS.Web.Plugins.Admin.ApiTokenManagement;

public partial class ApiTokenListPlugin
{
    private List<ApiTokenDetailResponse> ApiTokens { get; set; } = [];
    private ApiTokenDetailResponse? SelectedApiToken { get; set; } = default!;
    private Guid? ViewApiTokenId { get; set; } = default!;

    private string Mode { get; set; } = "Delete";

    private async Task Load()
    {
        var apiTokensResponse = await ApiClient.ApiToken.GetAllAsync();
        ApiTokens = apiTokensResponse?.Data?.ToList() ?? [];
    }

    protected override async Task OnInitializedAsync()
    {
        await Load();
    }

    private string MaskString(string input)
    {
        return new string('*', input.Length);
    }

    private async Task OnRegenerateConfirm(ApiTokenDetailResponse token)
    {
        SelectedApiToken = token;
        Mode = "Regenerate";
        // 
    }
    // OnRegenerateConfirm

    private async Task ViewToken(Guid id)
    {
        if (ViewApiTokenId == id)
        {
            ViewApiTokenId = default!;
        }
        else
        {
            ViewApiTokenId = id;
        }
    }

    private async Task OnConfirm(ApiTokenDetailResponse token)
    {
        SelectedApiToken = token;
        Mode = "Delete";
    }

    private async Task OnRegenerate()
    {
        await ApiClient.ApiToken.RegenerateSecretAsync(SelectedApiToken.Id);
        SelectedApiToken = default!;
        await Load();
    }

    private async Task OnDelete()
    {
        await ApiClient.ApiToken.DeleteAsync(SelectedApiToken.Id);
        SelectedApiToken = default!;
        await Load();
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
