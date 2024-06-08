namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public class StringFieldModel : IFieldModel
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = FieldTypes.STRING;
    public bool Required { get; set; }
    public string Label { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal DataTableColumnOrder { get; set; }
    public bool DataTableVisible { get; set; }
    public string DataTableViewComponent { get; set; } = nameof(StringFieldDataTableView);
    public decimal FormViewOrder { get; set; }
    public decimal FormColWidth { get; set; }
    public string FormViewComponent { get; set; } = nameof(StringFieldFormText);
}
