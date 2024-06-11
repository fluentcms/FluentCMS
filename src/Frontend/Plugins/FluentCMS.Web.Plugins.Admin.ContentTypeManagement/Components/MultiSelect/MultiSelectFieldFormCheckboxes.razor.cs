namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class MultiSelectFieldFormCheckboxes
{
    [CascadingParameter]
    private ContentTypeField? ContentTypeField { get; set; }

    [Parameter]
    public ICollection<string>? Value { get; set; }

    private List<string> Options { get; set; }

    private MultiSelectFieldModel? FieldModel { get; set; }

    protected override Task OnInitializedAsync()
    {
        if (ContentTypeField is not null)
            FieldModel = ContentTypeField.ToFieldModel<MultiSelectFieldModel>();
        
        if (Value is null)
            Value = FieldModel.DefaultValue ?? [];

        Options = FieldModel.Options.Split("\n").Select(x => x.Trim()).ToList();

        return base.OnInitializedAsync();
    }
}
