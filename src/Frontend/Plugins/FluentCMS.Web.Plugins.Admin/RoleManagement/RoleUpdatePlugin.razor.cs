namespace FluentCMS.Web.Plugins.Admin.RoleManagement;

public partial class RoleUpdatePlugin
{
    public const string FORM_NAME = "RoleUpdateForm";

    [SupplyParameterFromQuery(Name = "id")]
    private Guid Id { get; set; }

    private RoleUpdateRequest? Model { get; set; }

    private List<Policy>? Policies { get; set; }

    private RoleDetailResponse? Role { get; set; }

    protected override async Task OnInitializedAsync()
    {
    }

    private async Task OnSubmit()
    {
        await ApiClient.Role.UpdateAsync(Model);
        NavigateBack();
    }
}
