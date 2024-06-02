namespace FluentCMS.Web.UI.Plugins.ContentTypeManagement;

public partial class ContentTypeDetailPlugin
{
    [SupplyParameterFromQuery(Name = "id")]
    private Guid Id { get; set; }

    private State CurrentState { get; set; } = State.List;
    private ContentTypeDetailResponse? ContentType { get; set; }
    private ContentTypeField? SelectedField { get; set; }

    private List<FieldType> FieldTypes = new List<FieldType> {
        new FieldType {
            Icon = IconName.Text,
            Title = "Text",
            Description = "Small or long text like title or description",
            Key = "text"
        },
        new FieldType {
            Title = "Rich Text",
            Icon = IconName.Paragraph,
            Description = "A rich text editor with formatting options",
            Key = "rich-text"
        },
        new FieldType {
            Title = "Number",
            Icon = IconName.Number,
            Description = "Numbers (integer, float, decimal)",
            Key = "number"
        },
        new FieldType {
            Title = "Boolean",
            Icon = IconName.Boolean,
            Description = "Yes or no, 1 or 0, true or false",
            Key = "boolean"
        },
        // Add more types
    };

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
