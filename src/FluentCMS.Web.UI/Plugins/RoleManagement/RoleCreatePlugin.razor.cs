namespace FluentCMS.Web.UI.Plugins.RoleManagement;

public partial class RoleCreatePlugin
{
    public const string FORM_NAME = "RoleCreateForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private RoleCreateRequest? Model { get; set; } = new();

    private List<Policy> Policies { get; set; } = [];

    private List<Policy> ModelPolicies { get; set; } = [];

    protected override async Task OnInitializedAsync()
    {
        if (Policies.Count == 0)
        {
            var policiesResponse = await GetApiClient<RoleClient>().GetPoliciesAsync();
            Policies = policiesResponse?.Data?.ToList() ?? [];
        }

        if(ModelPolicies.Count == 0)
        {
            Model.Policies = [];
            ModelPolicies = Policies.Select(x => {
                return new Policy {
                    Area = x.Area,
                    Actions = []
                };
            }).ToList();
        }
    }

    private async Task OnSubmit()
    {
        Model.Policies = [];

        foreach(var policy in ModelPolicies) {
            Model.Policies.Add(policy);
        }
        
        await GetApiClient<RoleClient>().CreateAsync(Model);
        NavigateBack();
    }
}
