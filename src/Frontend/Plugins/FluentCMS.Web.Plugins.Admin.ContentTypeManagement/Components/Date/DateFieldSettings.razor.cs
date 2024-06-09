namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class DateFieldSettings
{
    private List<ComponentTypeOption> FormViewTypes
    {
        get => [new(nameof(DateFieldFormInput), "Date Input")];
    }

    private List<ComponentTypeOption> TableViewTypes
    {
        get => [new(nameof(DateFieldDataTableView), "Date String")];
    }
}
