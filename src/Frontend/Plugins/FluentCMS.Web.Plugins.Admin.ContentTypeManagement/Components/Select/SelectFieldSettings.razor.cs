namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class SelectFieldSettings
{
    private List<ComponentTypeOption> FormViewTypes
    {
        get => [new(nameof(SelectFieldFormSelect), "Select Input")];
    }

    private List<ComponentTypeOption> TableViewTypes
    {
        get => [new(nameof(SelectFieldDataTableBadge), "Badge")];
    }

    private List<string> SelectItems {
        get => Model.Options.Split("\n").Select(x => x.Trim()).ToList();
    }
}
