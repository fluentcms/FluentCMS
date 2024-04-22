namespace FluentCMS.Web.UI.Plugins.Auth;

public partial class ProfileViewPlugin
{
    [Inject]
    public AccountClient AccountClient { get; set; } = default!;

    public UserDetailResponse? View { get; set; }


    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        View = (await AccountClient.GetCurrentAsync()).Data;
    }
}
