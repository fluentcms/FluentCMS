namespace FluentCMS.Web.UI.Plugins.RoleManagement;

public partial class RoleCreatePlugin
{
    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private RoleCreateRequest Model { get; set; } = new();

    public const string FORM_NAME = "RoleCreateForm";

    private List<Policy> Policies { get; set; } = [];

    protected override async Task OnLoadAsync()
    {
        var policyResponse = await GetApiClient<RoleClient>().GetPoliciesAsync();
        Policies = policyResponse?.Data?.ToList() ?? [];
    }

    private async Task OnSubmit()
    {
        await GetApiClient<RoleClient>().CreateAsync(Model);
        NavigateBack();
    }
}