
namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public class BooleanFieldModel : FieldModel<bool>
{
    public string Label { get; set; } = string.Empty;
    public string? Description { get; set; }

    #region Overrides

    public override string Type { get; set; } = "string";

    public override Type GetFieldSettingComponentType()
    {
        return typeof(BooleanFieldSettings);
    }

    public override List<ComponentTypeOption> GetDataTableComponents()
    {
        return [
            new(typeof(BooleanFieldDataTableIndicator), "Indicator"),
            new(typeof(BooleanFieldDataTableTrueFalse), "True / False"),
            new(typeof(BooleanFieldDataTableYesNo), "Yes / No"),
            new(typeof(BooleanFieldDataTableSwitch), "Switch"),
        ];
    }

    public override List<ComponentTypeOption> GetFormComponents()
    {
        return [
            new(typeof(BooleanFieldFormCheckbox), "Checkbox"),
            new(typeof(BooleanFieldFormSwitch), "Switch"),
        ];
    }

    #endregion
}
