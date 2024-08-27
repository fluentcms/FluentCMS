namespace FluentCMS.Web.Plugins.Admin.UserManagement;

public partial class UserCreatePlugin
{
    public const string FORM_NAME = "UserCreateForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private UserCreateRequest? Model { get; set; }

    protected override async Task OnInitializedAsync()
    {
        Model ??= new();
    }

    private async Task OnSubmit()
    {
        Model!.RoleIds ??= [];
        await ApiClient.User.CreateAsync(Model);
        NavigateBack();
    }
}
