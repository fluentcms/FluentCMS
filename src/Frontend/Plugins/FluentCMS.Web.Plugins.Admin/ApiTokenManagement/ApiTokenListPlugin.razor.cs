using Microsoft.Extensions.Options;

namespace FluentCMS.Web.Plugins.Admin.ApiTokenManagement;

public partial class ApiTokenListPlugin
{
    [Inject]
    private IOptions<ClientSettings>? ClientSettingsOptions { get; set; }

    private List<ApiTokenDetailResponse> ApiTokens { get; set; } = [];
    private ApiTokenDetailResponse? SelectedApiToken { get; set; } = default!;
    private Guid? ViewApiTokenId { get; set; } = default!;

    private bool ShowDeleteModal { get; set; } = false;
    private bool ShowRegenerateModal { get; set; } = false;

    private async Task Load()
    {
        var apiTokensResponse = await ApiClient.ApiToken.GetAllAsync();
        ApiTokens = apiTokensResponse?.Data?.ToList() ?? [];
    }

    protected override async Task OnInitializedAsync()
    {
        await Load();
    }

    private async Task ToggleTokenKey(Guid id)
    {
        ViewApiTokenId = (ViewApiTokenId == id) ? default! : id;
        await Task.CompletedTask;
    }

    private async Task OnDeleteConfirm(ApiTokenDetailResponse token)
    {
        SelectedApiToken = token;
        ShowDeleteModal = true;
        await Task.CompletedTask;
    }
    private async Task OnDelete()
    {
        await ApiClient.ApiToken.DeleteAsync(SelectedApiToken!.Id);
        await OnConfirmComplete();
        await Load();
    }

    private async Task OnRegenerateConfirm(ApiTokenDetailResponse token)
    {
        SelectedApiToken = token;
        ShowRegenerateModal = true;
        await Task.CompletedTask;
    }

    private async Task OnRegenerate()
    {
        await ApiClient.ApiToken.RegenerateSecretAsync(SelectedApiToken!.Id);
        await OnConfirmComplete();
        await Load();
    }


    private async Task OnConfirmComplete()
    {
        SelectedApiToken = default!;
        ShowRegenerateModal = false;
        ShowDeleteModal = false;
        await Task.CompletedTask;
    }
}
