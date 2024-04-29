namespace FluentCMS.Web.UI.Plugins.Auth;

public partial class ProfileUpdatePlugin
{
    private AccountUpdateRequest Model { get; set; } = new();

    private UserDetailResponse View { get; set; } = new();

    protected override async Task OnInitializedAsync()
    {
        View = (await GetApiClient<AccountClient>().GetUserDetailsAsync()).Data;
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
        await GetApiClient<AccountClient>().UpdateUserDetailsAsync(Model);
        NavigateBack();
    }
}
