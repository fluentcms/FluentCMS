using FluentCMS.Web.ApiClients.Services;

namespace FluentCMS.Web.Plugins.Admin.Auth;

public partial class LogoutViewPlugin
{
    [Inject]
    private AuthManager AuthManager { get; set; } = default!;

    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        if (HttpContext != null)
            await AuthManager.Logout(HttpContext);

        NavigateTo("/");
    }
}
