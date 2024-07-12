namespace FluentCMS.Web.Plugins.Admin.RoleManagement;

public partial class RoleUpdatePlugin
{
    public const string FORM_NAME = "RoleUpdateForm";

    [SupplyParameterFromQuery(Name = "id")]
    private Guid Id { get; set; }

    private RoleRequest? Model { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (Model is null)
        {
            var roleResponse = await ApiClient.Role.GetByIdAsync(Id);
            Model = Mapper.Map<RoleRequest>(roleResponse.Data);
        }
    }

    private async Task OnSubmit()
    {
        await ApiClient.Role.UpdateAsync(Id, Model);
        NavigateBack();
    }
}
