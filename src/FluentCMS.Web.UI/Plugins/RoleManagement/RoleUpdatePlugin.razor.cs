namespace FluentCMS.Web.UI.Plugins.RoleManagement;

public partial class RoleUpdatePlugin
{
    public const string FORM_NAME = "RoleUpdateForm";

    [SupplyParameterFromQuery(Name = "id")]
    private Guid Id { get; set; }

    private RoleUpdateRequest Model { get; set; } = new();
    private List<Policy>? ModelPolicies { get; set; }

    private List<Policy> Policies { get; set; }

    private RoleDetailResponse Role { get; set; } = new();

    protected override async Task OnInitializedAsync()
    {
        if(Policies is null)
        {
            var policiesResponse = await GetApiClient<RoleClient>().GetPoliciesAsync();
            Policies = policiesResponse?.Data?.ToList() ?? [];
        }

        if(ModelPolicies is null)
        {
            var rolesResponse = await GetApiClient<RoleClient>().GetAllAsync();
            Role = rolesResponse.Data.ToList().Find(x => x.Id == Id);
            Model = Mapper.Map<RoleUpdateRequest>(Role);
            ModelPolicies = Model.Policies.ToList();
        }
    }

    private async Task OnSubmit()
    {
        await GetApiClient<RoleClient>().UpdateAsync(Model);
        NavigateBack();
    }
}
