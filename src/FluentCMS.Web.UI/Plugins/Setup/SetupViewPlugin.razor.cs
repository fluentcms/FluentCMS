using FluentCMS.Web.UI.Services;
using Microsoft.AspNetCore.Http;

namespace FluentCMS.Web.UI.Plugins.Setup;

public partial class SetupViewPlugin
{

    [SupplyParameterFromForm(FormName = "SetupForm")]
    public SetupRequest Model { get; set; } = new()
    {
        AppTemplateName = "Blank",
        SiteTemplateName = "Blank"
    };

    [Inject]
    private SetupManager SetupManager { get; set; } = default!;

    [Inject]
    private NavigationManager NavigationManager { get; set; } = default!;

    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    private string? Message { get; set; }

    private string? ErrorMessage { get; set; }

    private bool Initialized { get; set; } = false;

    protected override async Task OnInitializedAsync()
    {

        Initialized = await SetupManager.IsInitialized();

        try
        {
            if (HttpContext is null)
                throw new ArgumentNullException(nameof(HttpContext));

            if (!HttpMethods.IsPost(HttpContext.Request.Method))
            {
                Model.Username = "admin";
                Model.Email = "admin@example.com";
                Model.Password = "Passw0rd!";
                Model.AdminDomain = new Uri(NavigationManager.BaseUri).Authority;
            }
            else
            {
                // validate posted data
            }

        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    protected override void OnParametersSet()
    {

    }

    private async Task HandleSubmit()
    {
        try
        {
            if (await SetupManager.Start(Model))
            {
                Initialized = await SetupManager.IsInitialized();
                Message = "Setup completed!";
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.ToString();
        }
    }
}
