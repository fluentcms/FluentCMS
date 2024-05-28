namespace FluentCMS.Web.UI.Plugins.RoleManagement;

public partial class RoleListPlugin
{
    [Inject]
    public ConfirmService? ConfirmService { get; set; }

    private List<RoleDetailResponse> Roles { get; set; } = [];

    public async Task Load() {
        var rolesResponse = await GetApiClient<RoleClient>().GetAllAsync();
        Roles = rolesResponse?.Data?.ToList() ?? [];
    }

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        await Load();
    }

    public async Task OnDelete(RoleDetailResponse role)
    {
        var result = await ConfirmService!.Show("Are you sure to remove this Role?");

        if(result) {
            await GetApiClient<RoleClient>().DeleteAsync(role.Id);
            await Load();
        }
    }
}

