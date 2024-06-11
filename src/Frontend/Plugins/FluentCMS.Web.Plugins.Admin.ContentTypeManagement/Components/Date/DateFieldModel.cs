namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public class DateFieldModel : FieldModel<DateTime?>
{
    public string Label { get; set; } = string.Empty;
    public bool Required { get; set; }
    public string? Description { get; set; }
    public string? Placeholder { get; set; }
    public string? Format { get; set; }

    #region Overrides

    public override string Type { get; set; } = "datetime";

    public override Type GetFieldSettingComponentType()
    {
        return typeof(DateFieldSettings);
    }

    public override List<ComponentTypeOption> GetDataTableComponents()
    {
        return [new(typeof(DateFieldDataTableView), "Date String")];
    }

    public override List<ComponentTypeOption> GetFormComponents()
    {
        return [new(typeof(DateFieldFormInput), "Date Input")];
    }

    #endregion

}
