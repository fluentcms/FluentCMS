namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class SelectFieldFormSelect
{
    [CascadingParameter]
    private ContentTypeField? ContentTypeField { get; set; }

    [Parameter]
    public string? Value { get; set; }

    private List<string> SelectItems { get; set; }

    private SelectFieldModel? FieldModel { get; set; }

    protected override Task OnInitializedAsync()
    {
        if (ContentTypeField is not null)
            FieldModel = ContentTypeField.ToFieldModel<SelectFieldModel>();
        
        if (!string.IsNullOrEmpty(FieldModel.DefaultValue))
            Value = FieldModel.DefaultValue;

        SelectItems = FieldModel.Options.Split("\n").Select(x => x.Trim()).ToList();

        return base.OnInitializedAsync();
    }
}
