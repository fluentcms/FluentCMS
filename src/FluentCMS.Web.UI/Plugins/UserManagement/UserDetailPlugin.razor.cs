using FluentCMS.Web.ApiClients;
using FluentCMS.Web.UI.Services;
using Microsoft.AspNetCore.Components;

namespace FluentCMS.Web.UI.Plugins.UserManagement;
public partial class UserDetailPlugin : BasePlugin
{
    [Inject]
    public IHttpClientFactory HttpClientFactory { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "id")]
    Guid Id { get; set; }

    UserDetailResponse View { get; set; } = new();

    protected override async Task OnFirstAsync()
    {
        await base.OnFirstAsync();
        View = (await (HttpClientFactory.GetClient<UserClient>()).GetAsync(Id)).Data;
    }

}
