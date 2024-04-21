using FluentCMS.Web.UI.Services;

namespace FluentCMS.Web.UI.Plugins.UserManagement;

public partial class UserCreatePlugin
{
    [SupplyParameterFromForm(FormName = FORM_NAME)]
    UserCreateRequest Model { get; set; } = new();

    const string FORM_NAME = "UserCreateForm";

    [Inject]
    IHttpClientFactory HttpClientFactory { set; get; } = default!;

    string? Message { get; set; }
    public string BackUrl => new Uri(CurrentUrl).LocalPath;

    protected override void OnInitialized()
    {
        base.OnInitialized();
    }

    async Task OnSubmit()
    {
        try
        {
            var newUser = await HttpClientFactory.GetClient<UserClient>().CreateAsync(Model);
            Message = "Done!";
        }
        catch (Exception ex)
        {
            Message = ex.ToString();
        }
    }
}
