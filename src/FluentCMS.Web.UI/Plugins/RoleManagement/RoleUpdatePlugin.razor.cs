namespace FluentCMS.Web.UI.Plugins.RoleManagement;

public partial class RoleUpdatePlugin
{
    public const string FORM_NAME = "RoleUpdateForm";

    [SupplyParameterFromQuery(Name = "id")]
    private Guid Id { get; set; }

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private RoleUpdateRequest Model { get; set; } = new();

    private List<Policy> Policies { get; set; } = [];

    private RoleDetailResponse Role { get; set; } = new();

    protected override async Task OnInitializedAsync()
    {
        var policiesResponse = await GetApiClient<RoleClient>().GetPoliciesAsync();
        Policies = policiesResponse?.Data?.ToList() ?? [];

        var rolesResponse = await GetApiClient<RoleClient>().GetAllAsync();
        Role = rolesResponse.Data.ToList().Find(x => x.Id == Id);
        Model = new RoleUpdateRequest
        {
            Id = Id,
            Name = Role.Name,
            Description = Role.Description,
            Policies = [],
        };
    }

    private async Task OnSubmit()
    {
        await GetApiClient<RoleClient>().UpdateAsync(Model);
        NavigateBack();
    }
}
