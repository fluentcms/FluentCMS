namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class DateFieldFormInput
{
    [CascadingParameter]
    private ContentTypeField? ContentTypeField { get; set; }


    [Parameter]
    public decimal? Value { get; set; }

    private DateFieldModel? FieldModel { get; set; }

    protected override Task OnInitializedAsync()
    {
        if (ContentTypeField is not null)
            FieldModel = ContentTypeField.ToFieldModel<DateFieldModel>();

        return base.OnInitializedAsync();
    }
}
