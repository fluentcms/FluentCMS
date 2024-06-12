namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public class DateFieldModel : FieldModel<DateTime?>
{
    public override string Type { get; set; } = FieldTypes.DATE_TIME;
    public string Label { get; set; } = string.Empty;
    public bool Required { get; set; }
    public string? Description { get; set; }
    public string? Placeholder { get; set; }
    public string? Format { get; set; }
}
