namespace FluentCMS.Web.UI.Plugins.UserManagement;

public partial class UserUpdatePlugin
{

    [SupplyParameterFromQuery(Name = "id")]
    private Guid Id { get; set; }

    private AccountUpdateRequest Model { get; set; } = new();
    private UserDetailResponse User { get; set; } = new();

    protected override async Task OnLoadAsync()
    {
        var userResponse = await GetApiClient<UserClient>().GetAsync(Id);
        User = userResponse.Data;
        Model = new AccountUpdateRequest
        {
            Email = User.Email ?? string.Empty,
            FirstName = User.FirstName,
            LastName = User.LastName,
            PhoneNumber = User.PhoneNumber
        };
    }

    private async Task OnSubmit()
    {
        await GetApiClient<AccountClient>().UpdateCurrentAsync(Model);
        NavigateBack();
    }
}
