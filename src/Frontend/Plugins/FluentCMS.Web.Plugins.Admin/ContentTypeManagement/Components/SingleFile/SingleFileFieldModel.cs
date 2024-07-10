namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public class SingleFileFieldModel : FieldModel<Guid?>
{
    public override string Type { get; set; } = FieldTypes.SINGLE_FILE;
    public string? Placeholder { get; set; }
}
