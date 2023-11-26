
namespace FluentCMS.Web.UI.Pages;
public partial class Home
{
    public string? AccessToken{ get; set; } = "";
    public Guid? UserId{ get; set; } = Guid.Empty;
    public Guid[]? RoleIds{ get; set; } = [];
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);
        if (firstRender)
        {
            AccessToken = (await ProtectedLocalStorage.GetAsync<string>("access-token")).Value;
            UserId = (await ProtectedLocalStorage.GetAsync<Guid>("user-id")).Value;
            RoleIds = (await ProtectedLocalStorage.GetAsync<Guid[]>("role-ids")).Value;
            StateHasChanged();
        }
    }
}
