namespace FluentCMS.Web.Plugins.Admin.RoleManagement;

public partial class RoleCreatePlugin
{
    public const string FORM_NAME = "RoleCreateForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private RoleRequest Model { get; set; } = new();

    private async Task OnSubmit()
    {
        await ApiClient.Role.CreateAsync(Model);
        NavigateBack();
    }
}
