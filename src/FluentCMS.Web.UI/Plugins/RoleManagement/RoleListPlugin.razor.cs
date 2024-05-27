namespace FluentCMS.Web.UI.Plugins.RoleManagement;

public partial class RoleListPlugin
{
    private List<RoleDetailResponse> Roles { get; set; } = [];

    protected override async Task OnInitializedAsync()
    {
        var rolesResponse = await GetApiClient<RoleClient>().GetAllAsync();
        Roles = rolesResponse?.Data?.ToList() ?? [];
    }

    class DeleteRequest {
        public Guid Id { get; set; } 
    }

    public string GetDeleteCode(Guid id) 
    {
        return $"deleteIdInput.value='{id.ToString()}'";
    } 

    public const string FORM_NAME = "DeleteRoleForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    DeleteRequest DeleteModel { get; set; } = new();

    public async Task OnDelete() 
    {
        var res = await GetApiClient<RoleClient>().DeleteAsync(DeleteModel.Id);
        NavigateBack();
    }
}

