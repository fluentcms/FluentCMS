using FluentCMS.Web.UI.Services;

namespace FluentCMS.Web.UI.Plugins.UserManagement;
public partial class UserCreatePlugin
{
    const string _formName = "UserCreateForm";

    [Inject]
    NavigationManager NavigationManager { set; get; } = default!;

    [Inject]
    IHttpClientFactory HttpClientFactory { set; get; } = default!;
    UserClient UserClient { get; set; } = default!;

    string? Message { get; set; }
    public string BackUrl => new Uri(CurrentUrl).LocalPath;

    [SupplyParameterFromForm(FormName = _formName)]
    UserCreateRequest Model { get; set; } = new();
    protected override void OnInitialized()
    {
        base.OnInitialized();
        UserClient = HttpClientFactory.GetClient<UserClient>();
    }

    async Task OnSubmit()
    {
        try
        {
            var newUser = await UserClient.CreateAsync(Model);
            Message = "Done!";
        }
        catch (Exception ex)
        {
            Message = ex.ToString();
        }
    }
}
