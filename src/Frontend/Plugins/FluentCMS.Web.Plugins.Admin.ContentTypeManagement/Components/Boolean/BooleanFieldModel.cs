namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public class BooleanFieldModel : IFieldModel
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = FieldTypes.BOOLEAN;
    public string Label { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool DefaultValue { get; set; }
    public decimal DataTableColumnOrder { get; set; }
    public bool DataTableVisible { get; set; }

}
