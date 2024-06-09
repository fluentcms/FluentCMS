namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public class NumberFieldModel : IFieldModel
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = FieldTypes.NUMBER;
    public string Label { get; set; } = string.Empty;
    public bool Required { get; set; }
    public string? Description { get; set; }
    public string? Placeholder { get; set; }
    public decimal? MinimumValue { get; set; }
    public decimal? MaximumValue { get; set; }
    public decimal DataTableColumnOrder { get; set; }
    public bool DataTableVisible { get; set; }
    public string DataTableViewComponent { get; set; } = nameof(NumberFieldDataTableView);
    public decimal FormViewOrder { get; set; }
    public decimal FormColWidth { get; set; } = 12;
    public string FormViewComponent { get; set; } = nameof(NumberFieldFormInput);
    public decimal DefaultValue { get; set; }
}
