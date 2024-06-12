namespace FluentCMS.Web.Plugins.Admin.ContentTypeManagement;

public class StringFieldModel : FieldModel<string?>
{
    public override string Type { get; set; } = FieldTypes.STRING;
    public bool Required { get; set; }
    public string Label { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Placeholder { get; set; }
    public decimal? MinimumLength { get; set; }
    public decimal? MaximumLength { get; set; }
    public bool Unique { get; set; }
    public string? Pattern { get; set; }
}
