namespace FluentCMS.Web.UI.Plugins.RoleManagement;

public partial class RoleListPlugin
{
    private List<RoleDetailResponse> Roles { get; set; } = [];

    protected override async Task OnLoadAsync()
    {
        var rolesResponse = await GetApiClient<RoleClient>().GetAllAsync();
        Roles = rolesResponse?.Data?.ToList() ?? [];
    }

    protected void OnRowDefaultAction(Guid id)
    {
        var url = GetUrl("Update Role", new { id = id });
        NavigationManager.NavigateTo(url);
    }
}

