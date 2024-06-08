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
    public  string FormViewComponent { get; set; }
}
