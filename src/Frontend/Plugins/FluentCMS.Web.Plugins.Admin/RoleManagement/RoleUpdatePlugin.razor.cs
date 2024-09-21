namespace FluentCMS.Web.Plugins.Admin.RoleManagement;

public partial class RoleUpdatePlugin
{
    public const string FORM_NAME = "RoleUpdateForm";

    [SupplyParameterFromQuery(Name = "id")]
    private Guid Id { get; set; }

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private RoleUpdateRequest? Model { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (Model is null)
        {
            var roleResponse = await ApiClient.Role.GetByIdAsync(Id);
            Model = Mapper.Map<RoleUpdateRequest>(roleResponse.Data);
        }
    }

    private async Task OnSubmit()
    {
        await ApiClient.Role.UpdateAsync(Model);
        NavigateBack();
    }
}
