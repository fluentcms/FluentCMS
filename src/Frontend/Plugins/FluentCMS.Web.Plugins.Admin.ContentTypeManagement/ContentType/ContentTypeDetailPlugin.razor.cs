namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class ContentTypeDetailPlugin
{
    [SupplyParameterFromQuery(Name = "id")]
    private Guid Id { get; set; }

    private FieldManagementState CurrentState { get; set; } = FieldManagementState.List;
    private ContentTypeDetailResponse? ContentType { get; set; }
    private ContentTypeField? ContentTypeField { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await LoadList();
    }

    private async Task LoadList()
    {
        CurrentState = FieldManagementState.List;
        ContentTypeField = default;
        var contentTypesResponse = await GetApiClient<ContentTypeClient>().GetByIdAsync(Id);
        ContentType = contentTypesResponse?.Data;
    }

    private async Task OnCancel()
    {
        await LoadList();
    }

    private async Task OnEditFieldClick(ContentTypeField contentTypeField)
    {
        ContentTypeField = contentTypeField;
        CurrentState = FieldManagementState.Edit;
        await Task.CompletedTask;
    }


    private async Task OnCreateFieldClick()
    {
        ContentTypeField = new ContentTypeField()
        {
            Settings = new Dictionary<string, object?>()
        };
        CurrentState = FieldManagementState.Create;
        await Task.CompletedTask;
    }

    private async Task OnDeleteFieldClick(ContentTypeField contentTypeField)
    {
        ContentTypeField = contentTypeField;
        CurrentState = FieldManagementState.Delete;
        await Task.CompletedTask;
    }
}

public enum FieldManagementState
{
    List,
    Create,
    Edit,
    Delete
}
