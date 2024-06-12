namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public class MultiSelectFieldModel : FieldModel<ICollection<string>>
{
    public override string Type { get; set; } = FieldTypes.MULTI_SELECT;
    public bool Required { get; set; }
    public string Label { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Options { get; set; }
}