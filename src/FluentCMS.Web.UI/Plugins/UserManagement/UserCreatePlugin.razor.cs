using FluentCMS.Web.UI.Services;

namespace FluentCMS.Web.UI.Plugins.UserManagement;

public partial class UserCreatePlugin
{
    [SupplyParameterFromForm(FormName = FORM_NAME)]
    UserCreateRequest Model { get; set; } = new();

    const string FORM_NAME = "UserCreateForm";

    [Inject]
    IHttpClientFactory HttpClientFactory { set; get; } = default!;

    public string BackUrl => new Uri(CurrentUrl).LocalPath;

    protected override void OnInitialized()
    {
        base.OnInitialized();
    }

    async Task OnSubmit()
    {
        await HttpClientFactory.GetClient<UserClient>().CreateAsync(Model);
        NavigationManager.NavigateTo(BackUrl);
    }
}
