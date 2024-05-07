namespace FluentCMS.Web.UI.Plugins.RoleManagement;

public partial class RoleDetailPlugin
{
    [SupplyParameterFromQuery(Name = "id")]
    private Guid Id { get; set; }

    private RoleDetailResponse Role { get; set; } = new();

    private List<Policy> Policies { get; set; } = [];

    protected override async Task OnLoadAsync()
    {
        var apiResponse = await GetApiClient<RoleClient>().GetAllAsync();
        Role = apiResponse.Data?.ToList().Find(x => x.Id == Id);
        
        var policyResponse = await GetApiClient<RoleClient>().GetPoliciesAsync();
        Policies = policyResponse.Data?.ToList() ?? [];
    }
}
