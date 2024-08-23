namespace FluentCMS.Web.Plugins.Admin.UserManagement;

public partial class UserRoleAssignmentModal
{
    public const string FORM_NAME = "AssignRoleToUser";

    [Parameter]
    public Guid? SiteId { get; set; }

    [Parameter]
    public Guid? UserId { get; set; }

    [Parameter]
    public EventCallback<UserRoleUpdateRequest> OnSubmit { get; set; }

    [Parameter]
    public EventCallback OnCancel { get; set; }

    [Parameter]
    public List<UserRoleDetailResponse>? Roles { get; set; }

    private ICollection<Guid>? SelectedRoleIds { get; set; } = default!;

    [SupplyParameterFromForm(FormName = FORM_NAME)]
    private UserRoleUpdateRequest? Model { get; set; }

    protected override async Task OnInitializedAsync()
    {
        SelectedRoleIds = Roles?.Where(x => x.HasAccess).Select(x => x.RoleId).ToList();
        Model ??= new();
    }

    protected override Task OnParametersSetAsync()
    {
        SelectedRoleIds = Roles?.Where(x => x.HasAccess).Select(x => x.RoleId).ToList();
        return base.OnParametersSetAsync();
    }

    private async Task HandleSubmit()
    {
        Model!.RoleIds = SelectedRoleIds;
        Model!.SiteId = SiteId.Value;
        Model!.UserId = UserId.Value;

        await OnSubmit.InvokeAsync(Model);
    }

    private async Task HandleCancel()
    {
        await OnCancel.InvokeAsync();
    }

}
