namespace FluentCMS.Web.UI.Plugins.RoleManagement;

public partial class RoleUpdatePlugin
{

    [SupplyParameterFromQuery(Name = "id")]
    private Guid Id { get; set; }

    public const string FORM_NAME = "RoleUpdateForm";

    private RoleUpdateRequest Model { get; set; } = new();

    private RoleDetailResponse Role { get; set; } = new();

    private List<Policy> Policies { get; set; } = [];

    protected override async Task OnLoadAsync()
    {
        var roleResponse = await GetApiClient<RoleClient>().GetAllAsync();
        Role = roleResponse.Data?.ToList().Find(x => x.Id == Id);
        
        var policiesResponse = await GetApiClient<RoleClient>().GetPoliciesAsync();
        Policies = policiesResponse.Data?.ToList() ?? [];
        
        Model = new RoleUpdateRequest
        {
            Name = Role.Name,
            Description = Role.Description,
            Policies = Role.Policies,
            Id = Id,
        };
    }

    private async Task OnSubmit()
    {
        await GetApiClient<RoleClient>().UpdateAsync(Model);
        NavigateBack();
    }
}
