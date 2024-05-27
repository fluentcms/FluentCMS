namespace FluentCMS.Web.UI.Plugins.RoleManagement;

public partial class RoleCreatePlugin
{
    public const string FORM_NAME = "RoleCreateForm";

    private RoleCreateRequest? Model { get; set; } = new();
    private List<Policy>? ModelPolicies { get; set; } 

    private List<Policy>? Policies { get; set; } 

    protected override async Task OnInitializedAsync()
    {
        if (Policies is null)
        {
            var policiesResponse = await GetApiClient<RoleClient>().GetPoliciesAsync();
            Policies = policiesResponse?.Data?.ToList() ?? [];
        }

        if(ModelPolicies is null)
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
