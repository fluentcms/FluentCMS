namespace FluentCMS.Web.UI.Plugins.UserManagement;

public partial class UserUpdatePlugin
{
    public const string FORM_NAME = "UserUpdateForm";

    [SupplyParameterFromQuery(Name = "id")]
    private Guid Id { get; set; }

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private UserUpdateRequest Model { get; set; } = new();

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private ICollection<Guid> RoleIds { get; set; } = [];

    private List<RoleDetailResponse> Roles { get; set; } = [];

    private UserDetailResponse User { get; set; } = new();

    protected override async Task OnLoadAsync()
    {
        var rolesResponse = await GetApiClient<RoleClient>().GetAllAsync();
        Roles = rolesResponse?.Data?.ToList() ?? [];

        var userResponse = await GetApiClient<UserClient>().GetAsync(Id);
        User = userResponse.Data;
        Model = new UserUpdateRequest
        {
            Enabled = User.Enabled,
            FirstName = User.FirstName,
            LastName = User.LastName,
            PhoneNumber = User.PhoneNumber,
            Email = User.Email ?? string.Empty,
            Id = Id,
        };
    }

    private async Task OnSubmit()
    {
        await GetApiClient<UserClient>().UpdateAsync(Model);
        NavigateBack();
    }
}
