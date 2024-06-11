namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public interface IFieldModel
{
    public string Name { get; set; }
    public string Type { get; set; }
    public decimal DataTableColumnOrder { get; set; }
    public bool DataTableVisible { get; set; }
    public string DataTableViewComponent { get; set; }
    public decimal FormViewOrder { get; set; }
    public decimal FormColWidth { get; set; }
    public string FormViewComponent { get; set; }
}

public interface IFieldModel<T> : IFieldModel
{
    public T DefaultValue { get; set; }
}

public abstract class FieldModel<T> : IFieldModel<T>
{
    public string Name { get; set; } = string.Empty;
    public abstract string Type { get; set; }
    public decimal DataTableColumnOrder { get; set; } = 0;
    public bool DataTableVisible { get; set; } = true;
    public string DataTableViewComponent { get; set; } = default!;
    public decimal FormViewOrder { get; set; } = 0;
    public decimal FormColWidth { get; set; } = 12;
    public string FormViewComponent { get; set; } = default!;
    public T DefaultValue { get; set; } = default!;
}
