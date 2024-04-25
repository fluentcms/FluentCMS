namespace FluentCMS.Web.UI.Plugins.Auth;
public partial class ProfileUpdatePlugin
{
    AccountUpdateRequest Model { get; set; } = new();

    UserDetailResponse View { get; set; } = new();

    protected override async Task OnInitializedAsync()
    {
        View = (await GetApiClient<AccountClient>().GetUserDetailAsync()).Data;
        Model = new()
        {
            Email = View.Email,
            FirstName = View.FirstName,
            LastName = View.LastName,
            PhoneNumber = View.PhoneNumber,
        };
    }

    async Task OnSubmit()
    {
        await GetApiClient<AccountClient>().UpdateCurrentAsync(Model);
        NavigateBack();
    }
}
