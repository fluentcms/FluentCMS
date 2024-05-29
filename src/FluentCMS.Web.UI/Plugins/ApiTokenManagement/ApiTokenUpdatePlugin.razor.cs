namespace FluentCMS.Web.UI.Plugins.ApiTokenManagement;

public partial class ApiTokenUpdatePlugin
{
    public const string FORM_NAME = "ApiTokenUpdateForm";

    [SupplyParameterFromQuery(Name = "id")]
    private Guid Id { get; set; }

    private ApiTokenUpdateRequest? Model { get; set; }

    private List<Policy>? Policies { get; set; }

    private ApiTokenDetailResponse? ApiToken { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (Policies is null)
        {
            var policiesResponse = await GetApiClient<RoleClient>().GetPoliciesAsync();
            Policies = policiesResponse?.Data?.ToList() ?? [];
        }

        if (ApiToken is null)
        {
            var apiTokenResponse = await GetApiClient<ApiTokenClient>().GetByIdAsync(Id);
            ApiToken = apiTokenResponse.Data;
            Model = Mapper.Map<ApiTokenUpdateRequest>(ApiToken);

            Model.Policies = Policies.Select(x => new Policy
            {
                Area = x.Area,
                Actions = ApiToken?.Policies?.FirstOrDefault(y => y.Area == x.Area)?.Actions ?? []
            }).ToArray();
        }
    }

    private async Task OnSubmit()
    {
        await GetApiClient<ApiTokenClient>().UpdateAsync(Model);
        NavigateBack();
    }
}
