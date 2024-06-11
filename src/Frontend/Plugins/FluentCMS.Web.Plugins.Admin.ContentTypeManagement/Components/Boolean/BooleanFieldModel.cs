
namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public class BooleanFieldModel : FieldModel<bool>
{
    public override string Type { get; set; } = FieldTypes.BOOLEAN;
    public string Label { get; set; } = string.Empty;
    public string? Description { get; set; }
}
