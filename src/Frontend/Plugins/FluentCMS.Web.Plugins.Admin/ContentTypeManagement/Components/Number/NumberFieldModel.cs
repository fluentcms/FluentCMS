namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public class NumberFieldModel : FieldModel<double?>
{
    public override string Type { get; set; } = FieldTypes.NUMBER;
    public string? Placeholder { get; set; }
    public double? MinimumValue { get; set; }
    public double? MaximumValue { get; set; }
}
