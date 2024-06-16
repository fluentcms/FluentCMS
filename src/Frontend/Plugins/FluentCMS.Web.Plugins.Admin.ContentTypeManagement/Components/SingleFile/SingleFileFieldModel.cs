namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public class SingleFileFieldModel : FieldModel<Guid?>
{
    public override string Type { get; set; } = FieldTypes.SINGLE_FILE;
    public bool Required { get; set; }
    public string Label { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Placeholder { get; set; }
    public string? FileType { get; set; } = "other";
}
