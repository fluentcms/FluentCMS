using FluentCMS.Web.UI.Services;

namespace FluentCMS.Web.UI.Plugins.UserManagement;
public partial class UserListPlugin
{
    UserClient UserClient { get; set; } = default!;

    [Inject]
    IHttpClientFactory HttpClientFactory { get; set; } = default!;

    [Inject]
    ConfirmService Confirm { get; set; } = default!;

    public bool Loading { get; set; } = true;

    List<UserDetailResponse> Users { get; set; } = [];
    public Exception? Error { get; private set; } = null;

    protected override void OnInitialized()
    {
        base.OnInitialized();
        UserClient = HttpClientFactory.GetClient<UserClient>();
    }

    protected override async Task OnFirstAsync()
    {
        await LoadDataAsync();
        StateHasChanged();

    }

    private async Task LoadDataAsync()
    {
        try
        {
            Loading = true;
            if ((await UserClient!.GetAllAsync()) is var response && response != null && !response.Errors!.Any() && response.Data is var data && data != null)
            {
                Users = data.ToList();
            }
        }
        catch (Exception ex)
        {
            Error = ex;
            //throw;
        }
        finally
        {
            Loading = false;
        }
    }
}
