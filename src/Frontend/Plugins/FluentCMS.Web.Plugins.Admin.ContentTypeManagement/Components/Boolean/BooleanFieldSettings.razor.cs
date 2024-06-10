namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public partial class BooleanFieldSettings
{
    private List<ComponentTypeOption> FormViewTypes
    {
        get => [
            new(nameof(BooleanFieldFormCheckbox), "Checkbox"),
            new(nameof(BooleanFieldFormSwitch), "Switch"),
        ];
    }

    private List<ComponentTypeOption> TableViewTypes
    {
        get => [
            new(nameof(BooleanFieldDataTableIndicator), "Indicator"),
            new(nameof(BooleanFieldDataTableTrueFalse), "True / False"),
            new(nameof(BooleanFieldDataTableYesNo), "Yes / No"),
            new(nameof(BooleanFieldDataTableSwitch), "Switch"),
        ];
    }
}