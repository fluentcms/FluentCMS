namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public class NumberFieldModel : IFieldModel
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = FieldTypes.NUMBER;
    public string Label { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool DefaultValue { get; set; }
}
