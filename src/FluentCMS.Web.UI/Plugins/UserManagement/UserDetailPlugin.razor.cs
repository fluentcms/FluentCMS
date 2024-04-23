namespace FluentCMS.Web.UI.Plugins.UserManagement;

public partial class UserDetailPlugin
{
    [SupplyParameterFromQuery(Name = "id")]
    private Guid Id { get; set; }

    [Inject]
    public UserClient UserClient { get; set; } = default!;

    private UserDetailResponse User { get; set; } = new();

    protected override async Task OnLoadAsync()
    {
        var apiResponse = await UserClient.GetAsync(Id);
        User = apiResponse.Data;
    }
}
