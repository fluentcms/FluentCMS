using FluentCMS.Web.UI.Services;
using Microsoft.JSInterop;

namespace FluentCMS.Web.UI.Plugins.UserManagement;
public partial class UserUpdatePlugin : BasePlugin
{
    const string _formName = "UserUpdateForm";

    [Inject]
    IJSRuntime JsRuntime { set; get; } = default!;

    [Inject] IHttpClientFactory HttpClientFactory { set; get; } = default!;

    UserClient UserClient { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "id")]
    Guid Id { get; set; }

    string Message { get; set; } = string.Empty;

    public string BackUrl => new Uri(CurrentUrl).LocalPath;

    [SupplyParameterFromForm(FormName = _formName)]

    UserUpdateRequest Model { get; set; } = new();

    UserDetailResponse View { get; set; } = new();

    protected override Task OnInitializedAsync()
    {
        UserClient = HttpClientFactory.GetClient<UserClient>();
        return base.OnInitializedAsync();
    }

    protected override async Task OnParametersSetAsync()
    {
        try
        {
            View = (await UserClient.GetAsync(Id)).Data;
            Model.Id = Id;
            Model.Email = View.Email!;
        }
        catch (Exception)
        {
            Message = "An error occured!";
        }
    }
    protected override Task OnPostAsync()
    {
        Model.Id = Id;
        return base.OnPostAsync();
    }
    private async Task OnSubmit()
    {
        try
        {
            await UserClient.UpdateAsync(Model);
            Message = "Done!";
        }
        catch (Exception exception)
        {
            Message = exception.ToString();
        }
    }
}
