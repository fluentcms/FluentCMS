namespace FluentCMS.Web.UI.Plugins.RoleManagement;

public partial class RoleCreatePlugin
{
    public const string FORM_NAME = "RoleCreateForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private RoleCreateRequest Model { get; set; } = new();

    private List<Policy> Policies { get; set; } = [];

    protected override async Task OnLoadAsync()
    {
        var policiesResponse = await GetApiClient<RoleClient>().GetPoliciesAsync();
        Policies = policiesResponse?.Data?.ToList() ?? [];
        Model.Policies = [];
    }

    private async Task OnSubmit()
    {
        await GetApiClient<RoleClient>().CreateAsync(Model);
        NavigateBack();
    }
}
