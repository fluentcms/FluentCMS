namespace FluentCMS.Web.UI.Plugins.Auth;

public partial class ProfileViewPlugin
{
    private UserDetailResponse? View { get; set; }


    protected override async Task OnLoadAsync()
    {
        await base.OnLoadAsync();
        View = (await GetApiClient<AccountClient>().GetUserDetailsAsync()).Data;
    }
}
