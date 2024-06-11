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
    public List<ComponentTypeOption> GetFormComponents();
    public List<ComponentTypeOption> GetDataTableComponents();
    public Type GetFormComponentType();
    public Type GetDataTableComponentType();
    public Type GetFieldSettingComponentType();
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
    public abstract Type GetFieldSettingComponentType();
    public abstract List<ComponentTypeOption> GetFormComponents();
    public abstract List<ComponentTypeOption> GetDataTableComponents();

    public Type GetFormComponentType()
    {
        return GetFormComponents().Where(x => x.Name == FormViewComponent).First().Type;
    }

    public Type GetDataTableComponentType()
    {
        return GetDataTableComponents().Where(x => x.Name == DataTableViewComponent).First().Type;
    }

    public FieldModel()
    {
        DataTableViewComponent = GetDataTableComponents().First().Name;
        FormViewComponent = GetFormComponents().First().Name;
    }
}
