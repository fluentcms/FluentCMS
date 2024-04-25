namespace FluentCMS.Web.UI.Plugins.Auth;

public partial class ProfileViewPlugin
{

    public UserDetailResponse? View { get; set; }


    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        View = (await GetApiClient<AccountClient>().GetCurrentAsync()).Data;
    }
}
