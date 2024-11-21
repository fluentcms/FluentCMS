namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public class MultiFileFieldModel : FieldModel<ICollection<Guid>>
{
    public override string Type { get; set; } = FieldTypes.MULTI_FILE;
    public string? Placeholder { get; set; }
}
