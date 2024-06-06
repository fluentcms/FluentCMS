namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class StringFieldSettings
{

    [Parameter, EditorRequired]
    public StringFieldModel Model { get; set; } = default!;

    [Parameter, EditorRequired]
    public EventCallback OnCancel { get; set; }

    [Parameter, EditorRequired]
    public EventCallback<ContentTypeField> OnSubmit { get; set; }

    private async Task OnFieldCreate()
    {
        await OnSubmit.InvokeAsync(Model?.ToContentTypeField());
    }
}

public interface IFieldModel
{
    public string Name { get; set; }
    public string Type { get; set; }
}

public class StringFieldModel : IFieldModel
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "string";
    public bool Required { get; set; }
    public string Label { get; set; } = string.Empty;
    public string? Description { get; set; }
}
