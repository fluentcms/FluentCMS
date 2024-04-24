namespace FluentCMS.Web.UI.Plugins.UserManagement;

public partial class UserUpdatePlugin
{
    public const string FORM_NAME = "UserUpdateForm";

    [SupplyParameterFromQuery(Name = "id")]
    private Guid Id { get; set; }

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private UserUpdateRequest Model { get; set; } = new();

    private UserDetailResponse User { get; set; } = new();

    protected override async Task OnLoadAsync()
    {
        var userResponse = await GetApiClient<UserClient>().GetAsync(Id);
        User = userResponse.Data;
        Model = new UserUpdateRequest
        {
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
