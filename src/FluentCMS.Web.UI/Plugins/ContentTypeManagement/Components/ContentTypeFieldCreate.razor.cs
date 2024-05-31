namespace FluentCMS.Web.UI.Plugins.ContentTypeManagement;

public partial class ContentTypeFieldCreate
{
    public const string FORM_NAME = "ContentTypeFieldCreateForm";

    [Parameter]
    public ContentTypeField? Model { get; set; }

    [Parameter]
    public EventCallback OnSubmit { get; set; }

    [Parameter]
    public EventCallback OnCancel { get; set; }

    private string? SelectedType { get; set; }

    private async Task OnTypeSelect(string typeName)
    {
        Model!.Type = typeName;
        SelectedType = typeName;
        await Task.CompletedTask;
    }
}
