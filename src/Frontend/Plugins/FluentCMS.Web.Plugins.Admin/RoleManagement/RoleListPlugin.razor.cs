namespace FluentCMS.Web.Plugins.Admin.RoleManagement;

public partial class RoleListPlugin
{
    private List<RoleDetailResponse> Roles { get; set; } = default!;

    public async Task Load()
    {
        var rolesResponse = await ApiClient.Role.GetAllAsync(ViewState.Site.Id);
        Roles = rolesResponse?.Data?.ToList() ?? [];
    }

    protected override async Task OnInitializedAsync()
    {
        await Load();
    }

    #region Delete Role

    private RoleDetailResponse? SelectedRole { get; set; }
    public async Task OnDelete()
    {
        if (SelectedRole == null)
            return;

        await ApiClient.Role.DeleteAsync(SelectedRole.Id);
        await Load();
        SelectedRole = default;
    }

    public async Task OnConfirm(RoleDetailResponse role)
    {
        SelectedRole = role;
        await Task.CompletedTask;
    }
    public async Task OnConfirmClose()
    {
        SelectedRole = default;
        await Task.CompletedTask;
    }
    #endregion

}

