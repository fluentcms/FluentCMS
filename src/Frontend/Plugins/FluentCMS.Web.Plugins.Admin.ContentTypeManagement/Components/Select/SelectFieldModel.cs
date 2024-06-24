namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public class SelectFieldModel : FieldModel<string?>
{
    public override string Type { get; set; } = FieldTypes.SINGLE_SELECT;
    public string? Placeholder { get; set; }
    public string Options { get; set; }
}
