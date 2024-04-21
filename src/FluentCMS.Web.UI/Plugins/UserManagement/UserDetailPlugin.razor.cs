namespace FluentCMS.Web.UI.Plugins.UserManagement;

public partial class UserDetailPlugin
{
    [SupplyParameterFromQuery(Name = "id")]
    private Guid Id { get; set; }

    private UserDetailResponse User { get; set; } = new();

    protected override async Task OnLoadAsync()
    {
        var apiResponse = await HttpClientFactory.GetClient<UserClient>().GetAsync(Id);
        User = apiResponse.Data;
    }
}
