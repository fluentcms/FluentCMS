namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public interface IFieldModel
{
    public string Name { get; set; }
    public string Type { get; set; }
    public decimal DataTableColumnOrder { get; set; }
    public bool DataTableVisible { get; set; }
}
