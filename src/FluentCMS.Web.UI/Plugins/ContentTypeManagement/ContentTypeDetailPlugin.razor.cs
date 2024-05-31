namespace FluentCMS.Web.UI.Plugins.ContentTypeManagement;

public partial class ContentTypeDetailPlugin
{
    [SupplyParameterFromQuery(Name = "id")]
    private Guid Id { get; set; }

    private State CurrentState { get; set; } = State.List;
    private ContentTypeDetailResponse? ContentType { get; set; }
    private ContentTypeField? SelectedField { get; set; }

    protected override async Task OnInitializedAsync()
    {
        await LoadList();
    }

    private async Task LoadList()
    {
        CurrentState = State.List;
        SelectedField = default;
        var contentTypesResponse = await GetApiClient<ContentTypeClient>().GetByIdAsync(Id);
        ContentType = contentTypesResponse?.Data;
    }

    private async Task OnCancel()
    {
        await LoadList();
    }

    private async Task OnEditFieldClick(ContentTypeField contentTypeField)
    {
        SelectedField = contentTypeField;
        CurrentState = State.Edit;
        await Task.CompletedTask;
    }
    private async Task OnDeleteFieldClick(ContentTypeField contentTypeField)
    {
        SelectedField = contentTypeField;
        CurrentState = State.Delete;

        if (!string.IsNullOrEmpty(SelectedField?.Name) && ContentType != null)
            await GetApiClient<ContentTypeClient>().DeleteFieldAsync(ContentType.Id, SelectedField.Name);

        await LoadList();
    }

    private async Task OnCreateFieldClick()
    {
        SelectedField = new ContentTypeField();
        CurrentState = State.Create;
        await Task.CompletedTask;
    }

    private async Task OnFieldUpdate()
    {
        if (!string.IsNullOrEmpty(SelectedField?.Name) && ContentType != null)
            await GetApiClient<ContentTypeClient>().SetFieldAsync(ContentType.Id, SelectedField);

        await LoadList();
    }

    private enum State
    {
        List,
        Create,
        Edit,
        Delete
    }
}
