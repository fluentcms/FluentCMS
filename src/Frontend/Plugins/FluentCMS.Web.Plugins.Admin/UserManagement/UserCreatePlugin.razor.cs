namespace FluentCMS.Web.Plugins.Admin.UserManagement;

public partial class UserCreatePlugin
{
    public const string FORM_NAME = "UserCreateForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private UserCreateRequest? Model { get; set; }

    protected override async Task OnInitializedAsync()
    {
        Model ??= new();
        await base.OnInitializedAsync();
    }

    private async Task OnSubmit()
    {
        await ApiClient.User.CreateAsync(Model);
        NavigateBack();
    }
}
