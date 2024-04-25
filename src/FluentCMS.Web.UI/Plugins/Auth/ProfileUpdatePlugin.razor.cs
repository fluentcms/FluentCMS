namespace FluentCMS.Web.UI.Plugins.Auth;
public partial class ProfileUpdatePlugin
{
    UserUpdateRequest Model { get; set; } = new();

    UserDetailResponse View { get; set; } = new();

    protected override async Task OnInitializedAsync()
    {
        View = (await GetApiClient<AccountClient>().GetCurrentAsync()).Data;
        Model = new()
        {
            Email = View.Email,
            FirstName = View.FirstName,
            Id = View.Id,
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
