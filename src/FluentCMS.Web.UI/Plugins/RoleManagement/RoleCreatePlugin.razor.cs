namespace FluentCMS.Web.UI.Plugins.RoleManagement;

public partial class RoleCreatePlugin
{
    public const string FORM_NAME = "RoleCreateForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private RoleCreateRequest Model { get; set; } = new();

    private List<Policy> Policies { get; set; } = [];

    private List<Policy> ModelPolicies { get; set; } = [];

    protected override async Task OnInitializedAsync()
    {
        var policiesResponse = await GetApiClient<RoleClient>().GetPoliciesAsync();
        Policies = policiesResponse?.Data?.ToList() ?? [];
        
        ModelPolicies = Policies.Select(x => {
            Console.WriteLine(x.Area);
            return new Policy {
                Area = x.Area,
                Actions = []
            };
        }).ToList();
    }

    private async Task OnSubmit()
    {
        Model.Policies = [];

        Console.WriteLine($"OnSubmit {Model.Policies.Count} - {ModelPolicies.Count}");
        Console.WriteLine($"OnSubmit {ModelPolicies[0].Actions.Count}");
        foreach(var policy in ModelPolicies) {
            Console.WriteLine(policy.Actions.Count);
            Model.Policies.Add(policy);
        }
        Console.WriteLine($"Model.Policies {Model.Policies.Count}");

        
        await GetApiClient<RoleClient>().CreateAsync(Model);
        NavigateBack();
    }
}
