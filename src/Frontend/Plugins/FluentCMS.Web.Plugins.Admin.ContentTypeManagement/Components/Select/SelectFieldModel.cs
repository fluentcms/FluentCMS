namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public class SelectFieldModel : FieldModel<string?>
{
    public override string Type { get; set; } = FieldTypes.SELECT;
    public bool Required { get; set; }
    public string Label { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Placeholder { get; set; }
    public string Options { get; set; }
}
