namespace FluentCMS.Web.UI.Plugins.RoleManagement;

public partial class RoleListPlugin
{
    [Inject]
    public ConfirmService Confirm { get; set; } 

    private List<RoleDetailResponse> Roles { get; set; } = [];

    protected override async Task OnLoadAsync()
    {
        var rolesResponse = await GetApiClient<RoleClient>().GetAllAsync();
        Roles = rolesResponse?.Data?.ToList() ?? [];
    }

    protected void OnRowDefaultAction(Guid Id)
    {
        var url = GetUrl("Role Detail", new { id = Id });
        NavigationManager.NavigateTo(url);
    }

    protected async Task OnDelete(Guid Id)
    {
        var result = await Confirm.Show("Are you sure you want to remove this Role?");
        if (!result) return;
        await GetApiClient<RoleClient>().DeleteAsync(Id);
        await OnLoadAsync();
    }
}
