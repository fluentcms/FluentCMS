namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public class NumberFieldModel : FieldModel<decimal?>
{
    public string Label { get; set; } = string.Empty;
    public bool Required { get; set; }
    public string? Description { get; set; }
    public string? Placeholder { get; set; }
    public decimal? MinimumValue { get; set; }
    public decimal? MaximumValue { get; set; }

    #region Overrides

    public override string Type { get; set; } = "decimal";

    public override Type GetFieldSettingComponentType()
    {
        return typeof(NumberFieldSettings);
    }

    public override List<ComponentTypeOption> GetDataTableComponents()
    {
        return [new(typeof(NumberFieldDataTableView), "Number")];
    }

    public override List<ComponentTypeOption> GetFormComponents()
    {
        return [
            new(typeof(NumberFieldFormInput), "Number Input"),
            new(typeof(NumberFieldFormRange), "Range Input"),
        ];
    }

    #endregion
}
