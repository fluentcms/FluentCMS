namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class ContentTypeFieldCreate
{
    public const string FORM_NAME = "ContentTypeFieldCreateForm";

    [Parameter]
    public ContentTypeField? Model { get; set; }

    [Parameter]
    public EventCallback OnSubmit { get; set; }

    [Parameter]
    public EventCallback OnCancel { get; set; }

    private FieldType? FieldType { get; set; }

    private FieldTypes FieldTypes { get; set; } = [];

    protected override async Task OnInitializedAsync()
    {
        Model ??= new();
        Model.Settings ??= new Dictionary<string, object?>();
        await Task.CompletedTask;
    }

    private async Task OnBack()
    {
        Model!.Type = default!;
        FieldType = default!;
        await Task.CompletedTask;
    }

    private async Task OnTypeSelect(FieldType type)
    {
        Model!.Type = type.Key;
        FieldType = type;
        await Task.CompletedTask;
    }

    private async Task HandleSubmit()
    {
        await OnSubmit.InvokeAsync();
    }
}
