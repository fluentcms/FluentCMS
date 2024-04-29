namespace FluentCMS.Web.UI.Plugins.UserManagement;

public partial class UserUpdatePlugin
{

    [SupplyParameterFromQuery(Name = "id")]
    private Guid Id { get; set; }

    private UserUpdateRequest Model { get; set; } = new();

    private UserDetailResponse User { get; set; } = new();

    protected override async Task OnLoadAsync()
    {
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
