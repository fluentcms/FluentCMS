namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class MultiSelectFieldSettings
{
    private List<ComponentTypeOption> FormViewTypes
    {
        get => [new(nameof(MultiSelectFieldFormCheckboxes), "Checkboxes")];
    }

    private List<ComponentTypeOption> TableViewTypes
    {
        get => [new(nameof(MultiSelectFieldDataTableBadges), "Badges")];
    }

    private List<string> Options {
        get => Model.Options.Split("\n").Select(x => x.Trim()).ToList();
    }
}
