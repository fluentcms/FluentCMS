namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public class StringFieldModel : IFieldModel
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = FieldTypes.STRING;
    public bool Required { get; set; }
    public string Label { get; set; } = string.Empty;
    public string? Description { get; set; }
}
