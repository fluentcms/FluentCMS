namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public class StringFieldModel : FieldModel<string?>
{
    public override string Type { get; set; } = FieldTypes.STRING;
    public string? Placeholder { get; set; }
    public int? MinimumLength { get; set; }
    public int? MaximumLength { get; set; }
    public string? Pattern { get; set; }
}
