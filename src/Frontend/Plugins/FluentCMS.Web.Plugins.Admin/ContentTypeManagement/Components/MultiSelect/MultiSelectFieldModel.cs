namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public class MultiSelectFieldModel : FieldModel<ICollection<string>>
{
    public override string Type { get; set; } = FieldTypes.MULTI_SELECT;
    public string Options { get; set; } = string.Empty;
}
