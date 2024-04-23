namespace FluentCMS.Web.UI.Plugins.UserManagement;

public partial class UserUpdatePlugin
{
    const string FORM_NAME = "UserUpdateForm";

    [SupplyParameterFromQuery(Name = "id")]
    private Guid Id { get; set; }

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private UserUpdateRequest Model { get; set; } = new();

    private UserDetailResponse User { get; set; } = new();

    [Inject]
    public UserClient UserClient { get; set; } = default!;

    protected override async Task OnLoadAsync()
    {
        var userResponse = await UserClient.GetAsync(Id);
        User = userResponse.Data;
        Model = new UserUpdateRequest
        {
            Email = User.Email ?? string.Empty,
            Id = Id,
        };
    }

    protected override Task OnPostAsync()
    {
        return Task.CompletedTask;
    }

    private async Task OnSubmit()
    {
        await UserClient.UpdateAsync(Model);
        NavigateBack();
    }
}
