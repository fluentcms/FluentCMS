namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class ContentTypeFieldCreate
{
    public const string FORM_NAME = "ContentTypeFieldCreateForm";

    [Parameter]
    public ContentTypeField? Model { get; set; }

    private FieldType? SelectedFieldType { get; set; }

    private FieldTypes FieldTypes { get; set; } = [];

    protected override async Task OnInitializedAsync()
    {
        Model ??= new();
        Model.Settings ??= new Dictionary<string, object?>();
        await Task.CompletedTask;
    }

    private async Task OnBackToTypeSelector()
    {
        Model!.Type = default!;
        SelectedFieldType = default!;
        await Task.CompletedTask;
    }

    private async Task OnTypeSelect(FieldType type)
    {
        Model!.Type = type.Key;
        SelectedFieldType = type;
        await Task.CompletedTask;
    }

    private async Task OnFieldCreate()
    {
        Model!.Type = default!;
        SelectedFieldType = default!;
        if (!string.IsNullOrEmpty(Model?.Name) && ContentType != null)
            await GetApiClient().SetFieldAsync(ContentType.Id, Model);

        await OnComplete.InvokeAsync();
    }
}
