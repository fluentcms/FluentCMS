namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class DateFieldDataTableView
{
    [CascadingParameter]
    private ContentTypeField? ContentTypeField { get; set; }

    private DateFieldModel? FieldModel { get; set; }

    private DateTime? _value { get; set; }

    protected override Task OnInitializedAsync()
    {
        if (ContentTypeField is not null)
            FieldModel = ContentTypeField.ToFieldModel<DateFieldModel>();

        if (Value != null)
            _value = DateTime.Parse(Value);

        return base.OnInitializedAsync();
    }

    [Parameter]
    public string? Value { get; set; }
}
