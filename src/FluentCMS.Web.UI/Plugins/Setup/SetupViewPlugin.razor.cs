namespace FluentCMS.Web.UI.Plugins.Setup;

public partial class SetupViewPlugin
{
    const string FORM_NAME = "SetupForm";

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    public SetupRequest Model { get; set; } = new();

    [Inject]
    private SetupManager SetupManager { get; set; } = default!;

    [Inject]
    private IAuthService AuthService { get; set; } = default!;

    private bool Initialized { get; set; } = false;

    protected override async Task OnLoadAsync()
    {
        Initialized = await SetupManager.IsInitialized();
        Model = new SetupRequest
        {
            Username = "admin",
            Email = "admin@example.com",
            Password = "Passw0rd!",
            AdminDomain = new Uri(NavigationManager.BaseUri).Authority,
            AppTemplateName = "Blank",
            SiteTemplateName = "Blank"
        };
        await Task.CompletedTask;
    }

    private async Task OnSubmit()
    {
        if (await SetupManager.Start(Model))
        {
            Initialized = await SetupManager.IsInitialized();
            await AuthService.Login(HttpContext, Model.Username, Model.Password, true);
        }
    }
}
