using FluentCMS.Web.UI.Services;

namespace FluentCMS.Web.UI.Plugins.UserManagement;
public partial class UserUpdatePlugin
{
    const string FORM_NAME = "UserUpdateForm";

    [Inject] IHttpClientFactory HttpClientFactory { set; get; } = default!;

    [SupplyParameterFromQuery(Name = "id")]
    Guid Id { get; set; }

    public string BackUrl => new Uri(CurrentUrl).LocalPath;

    [SupplyParameterFromForm(FormName = FORM_NAME)]

    UserUpdateRequest Model { get; set; } = new();

    UserDetailResponse View { get; set; } = new();

    protected override async Task OnFirstAsync()
    {
        await base.OnFirstAsync();
        View = (await (HttpClientFactory.GetClient<UserClient>()).GetAsync(Id)).Data;
        Model.Id = View.Id;
        Model.Email = View.Email!;
        Model.Enabled = View.Enabled;
        Model.PhoneNumber = View.PhoneNumber;
    }
    protected override Task OnPostAsync()
    {
        Model.Id = Id;

        return base.OnPostAsync();
    }
    private async Task OnSubmit()
    {
        await (HttpClientFactory.GetClient<UserClient>()).UpdateAsync(Model);
        NavigationManager.NavigateTo(BackUrl);
    }
}
