namespace FluentCMS.Web.UI.Plugins.Auth;

public partial class ProfileUpdatePlugin
{
    public AccountUpdateRequest Model { get; set; } = new()
    {
        Email = "",
        FirstName = "",
        LastName = "",
        PhoneNumber = ""
    };

    protected override async Task OnLoadAsync()
    {
        await base.OnLoadAsync();
        var user = (await GetApiClient<AccountClient>().GetCurrentAsync()).Data;
        Model = new AccountUpdateRequest()
        {
            Email = user.Email ?? string.Empty,
            FirstName = user.FirstName,
            LastName = user.LastName,
            PhoneNumber = user.PhoneNumber,
        };
    }

    private async Task OnSubmit()
    {
        await GetApiClient<AccountClient>().UpdateCurrentAsync(Model);
        NavigateBack();
    }
}
