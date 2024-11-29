namespace FluentCMS.Web.Plugins.Admin.RoleManagement;

public partial class RoleCreatePlugin
{
    public const string FORM_NAME = "RoleCreateForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private RoleCreateRequest? Model { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        Model ??= new RoleCreateRequest();
        Model.SiteId = ViewState.Site.Id;
        await Task.CompletedTask;
    }

    private async Task OnSubmit()
    {
        await ApiClient.Role.CreateAsync(Model);
        NavigateBack();
    }
}
