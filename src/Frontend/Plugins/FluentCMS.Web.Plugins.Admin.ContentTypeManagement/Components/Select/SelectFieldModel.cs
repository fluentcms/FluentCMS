namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public class SelectFieldModel : IFieldModel
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = FieldTypes.SELECT;
    public string Label { get; set; } = string.Empty;
    public bool Required { get; set; }
    public string? Description { get; set; }
    public string? Placeholder { get; set; }
    public decimal DataTableColumnOrder { get; set; }
    public bool DataTableVisible { get; set; }
    public string DataTableViewComponent { get; set; } = nameof(SelectFieldDataTableBadge);
    public decimal FormViewOrder { get; set; }
    public decimal FormColWidth { get; set; }
    public string FormViewComponent { get; set; } = nameof(SelectFieldFormSelect);
    public string? DefaultValue { get; set; }
    public string Options { get; set; }
}
