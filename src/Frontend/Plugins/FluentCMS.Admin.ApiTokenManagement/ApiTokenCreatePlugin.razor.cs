namespace FluentCMS.Admin.ApiTokenManagement;

public partial class ApiTokenCreatePlugin
{
    public const string FORM_NAME = "ApiTokenCreateForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private ApiTokenCreateRequest Model { get; set; } = new();

    private List<Policy>? Policies { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (Policies is null)
        {
            var policiesResponse = await GetApiClient<RoleClient>().GetPoliciesAsync();
            Policies = policiesResponse?.Data?.ToList() ?? [];
        }

        if (Model.Policies == null || Model.Policies.Count == 0)
        {
            Model.Policies = Policies.Select(x =>
            {
                return new Policy
                {
                    Area = x.Area,
                    Actions = []
                };
            }).ToArray();
        }
    }

    private async Task OnSubmit()
    {
        await GetApiClient<ApiTokenClient>().CreateAsync(Model);
        NavigateBack();
    }
}
