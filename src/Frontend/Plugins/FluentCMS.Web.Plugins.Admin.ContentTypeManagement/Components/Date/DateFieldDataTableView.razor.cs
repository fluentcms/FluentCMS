namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class DateFieldDataTableView
{
    [CascadingParameter]
    private ContentTypeField? ContentTypeField { get; set; }

    private DateFieldModel? FieldModel { get; set; }

    protected override Task OnInitializedAsync()
    {
        if (ContentTypeField is not null)
            FieldModel = ContentTypeField.ToFieldModel<DateFieldModel>();

        return base.OnInitializedAsync();
    }

    [Parameter]
    public DateTime? Value { get; set; }
}
