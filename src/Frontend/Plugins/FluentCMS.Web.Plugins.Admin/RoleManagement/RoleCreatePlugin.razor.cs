namespace FluentCMS.Web.Plugins.Admin.RoleManagement;

public partial class RoleCreatePlugin
{
    public const string FORM_NAME = "RoleCreateForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private RoleCreateRequest Model { get; set; } = new();

    private List<Policy>? Policies { get; set; }

    protected override async Task OnInitializedAsync()
    {
        Model.SiteId = ViewState.Site.Id;
    }

    private async Task OnSubmit()
    {
        await ApiClient.Role.CreateAsync(Model);
        NavigateBack();
    }
}
