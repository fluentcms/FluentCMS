namespace FluentCMS.Web.UI.Plugins.RoleManagement;

public partial class RoleListPlugin
{
    private List<RoleDetailResponse> Roles { get; set; } = [];

    public async Task Load()
    {
        var rolesResponse = await GetApiClient<RoleClient>().GetAllAsync();
        Roles = rolesResponse?.Data?.ToList() ?? [];
    }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        await Load();
    }

    public async Task OnDelete(Guid id)
    {
        await GetApiClient<RoleClient>().DeleteAsync(id);
        await Load();
    }
}

