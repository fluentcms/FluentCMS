namespace FluentCMS.Web.UI.Plugins.RoleManagement;

public partial class RoleListPlugin
{
    private List<RoleDetailResponse> Roles { get; set; } = [];

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        var rolesResponse = await GetApiClient<RoleClient>().GetAllAsync();
        Roles = rolesResponse?.Data?.ToList() ?? [];
    }

    class DeleteRequest {
        public Guid Id { get; set; } 
    }

    private const string FORM_NAME = "DeleteRoleForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private DeleteRequest DeleteModel { get; set; } = new();

    private async Task OnDelete() 
    {
        var res = await GetApiClient<RoleClient>().DeleteAsync(DeleteModel.Id);
        NavigateBack();
    }
}

