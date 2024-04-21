using FluentCMS.Web.UI.Services;

namespace FluentCMS.Web.UI.Plugins.Setup;

public partial class SetupViewPlugin
{
    const string _formName = "SetupForm";

    [SupplyParameterFromForm(FormName = _formName)]
    public SetupRequest Model { get; set; } = new();

    [Inject]
    private SetupManager SetupManager { get; set; } = default!;

    private bool Initialized { get; set; } = false;

    protected override async Task OnInitializedAsync()
    {
        await base.OnInitializedAsync();
        Initialized = await SetupManager.IsInitialized();
    }

    protected override async Task OnFirstAsync()
    {
        await base.OnFirstAsync();

        Model = new SetupRequest
        {
            Username = "admin",
            Email = "admin@example.com",
            Password = "Passw0rd!",
            AdminDomain = new Uri(NavigationManager.BaseUri).Authority,
            AppTemplateName = "Blank",
            SiteTemplateName = "Blank"
        };
    }

    private async Task OnSubmit()
    {
        if (await SetupManager.Start(Model))
        {
            Initialized = await SetupManager.IsInitialized();
        }
    }
}
