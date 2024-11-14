using FluentCMS.Web.ApiClients.Services;
using Microsoft.AspNetCore.Http;

namespace FluentCMS.Web.UI;

public partial class SetupView
{
    public const string FORM_NAME = "SetupForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    public SetupRequest? Model { get; set; }

    [Inject]
    private SetupManager SetupManager { get; set; } = default!;

    [Inject]
    private IHttpContextAccessor HttpContextAccessor { get; set; } = default!;

    [Inject]
    private ApiClientFactory ApiClient { get; set; } = default!;

    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    [Inject]
    private AuthManager AuthManager { get; set; } = default!;

    [CascadingParameter]
    protected HttpContext HttpContext { get; set; } = default!;
    private List<string>? Templates { get; set; }

    protected override async Task OnParametersSetAsync()
    {
        if (await SetupManager.IsInitialized())
            throw new InvalidOperationException("Setup is already complete.");

        Model ??= new SetupRequest
        {
            Username = "admin",
            Email = "admin@example.com",
            Password = "Passw0rd!",
            Url = new Uri(NavigationManager.BaseUri).Authority,
            Template = "Default"
        };

        if (Templates is null)
        {
            var templatesResponse = await ApiClient.Setup.GetTemplatesAsync();
            Templates = templatesResponse?.Data?.ToList() ?? [];
        }
    }

    protected virtual void NavigateTo(string path, bool forceLoad = false)
    {
        if (HttpContextAccessor?.HttpContext != null && !HttpContextAccessor.HttpContext.Response.HasStarted)
            HttpContextAccessor.HttpContext.Response.Redirect(path);
        else
            NavigationManager.NavigateTo(path, forceLoad);
    }

    private async Task OnSubmit()
    {
        if (await SetupManager.Start(Model!))
        {
            await AuthManager.Logout(HttpContext);
            await AuthManager.Login(HttpContext, Model!.Username, Model.Password, true);
            NavigateTo("/");
        }
    }
}
