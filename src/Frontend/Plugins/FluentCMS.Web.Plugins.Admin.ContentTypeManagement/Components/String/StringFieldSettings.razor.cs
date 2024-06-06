namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement.Components.String;

public partial class StringFieldSettings
{

    [Parameter, EditorRequired]
    public ContentTypeField? Field
    {
        get => Model.ToContentTypeField();
        set => Model = value?.ToFieldModel<StringFieldModel>() ?? new StringFieldModel();
    }

    public StringFieldModel? Model { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (Model == null)
        {
            Model = Field?.ToFieldModel<StringFieldModel>() ?? new StringFieldModel();
        }
        await Task.CompletedTask;
    }


}
public interface IFieldModel
{
    public string Name { get; set; }
    public string Type { get; set; }
}

public class StringFieldModel : IFieldModel
{
    public string Name { get; set; } = default!;
    public string Type { get; set; } = "string";
    public bool Required { get; set; }
    public string Label { get; set; } = default!;
    public string? Description { get; set; }
}
