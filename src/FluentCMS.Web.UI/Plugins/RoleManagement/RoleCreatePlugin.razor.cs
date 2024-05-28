namespace FluentCMS.Web.UI.Plugins.RoleManagement;

public partial class RoleCreatePlugin
{
    public const string FORM_NAME = "RoleCreateForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private RoleCreateRequest Model { get; set; } = new();

    private List<Policy>? Policies { get; set; } 

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        if (Policies is null)
        {
            var policiesResponse = await GetApiClient<RoleClient>().GetPoliciesAsync();
            Policies = policiesResponse?.Data?.ToList() ?? [];
        }

        if(Model.Policies == null || Model.Policies.Count == 0)
        {
            Model.Policies = Policies.Select(x => {
                return new Policy {
                    Area = x.Area,
                    Actions = []
                };
            }).ToArray();
        }
    }

    private async Task OnSubmit()
    {
        await GetApiClient<RoleClient>().CreateAsync(Model);
        NavigateBack();
    }
}
